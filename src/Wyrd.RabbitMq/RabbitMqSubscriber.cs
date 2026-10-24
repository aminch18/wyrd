using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Wyrd.Runtime;

namespace Wyrd.RabbitMq;

/// <summary>
/// Delivers to a runtime what other services publish: a durable queue of this service, bound to the contracts the runtime
/// uses (<see cref="IWyrdRuntime.Subscriptions"/>), consumed with manual acknowledgements. A message is acknowledged
/// once the runtime has it (<see cref="IWyrdRuntime.ReceiveAsync"/>), so a crash before that delivers it again, and the
/// runtime handles the redelivery once. A message the runtime can't take now is handed back after
/// <see cref="RabbitMqOptions.RetryDelay"/>; one that isn't a Wyrd message, or whose payload can never be read, is
/// rejected (to the queue's dead-letter exchange, if it has one). Bindings are only ever added:
/// unbind a contract the service stopped using by hand.
/// </summary>
public sealed partial class RabbitMqSubscriber : IAsyncDisposable
{
    private readonly IChannel _channel;
    private readonly IWyrdRuntime _runtime;
    private readonly RabbitMqOptions _options;
    private readonly ILogger _logger;
    private string? _consumerTag;

    private RabbitMqSubscriber(IChannel channel, IWyrdRuntime runtime, RabbitMqOptions options, ILogger logger)
    {
        _channel = channel;
        _runtime = runtime;
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Declares the exchange and <paramref name="queue"/> (one per service: its instances share it), binds the queue to
    /// every contract <paramref name="runtime"/> uses and starts consuming. <paramref name="connection"/> stays yours.
    /// The bindings are those of the runtime as registered now: register every process, projection and upcaster first,
    /// since a contract used only by something registered later has no binding and the broker drops it.
    /// </summary>
    public static async Task<RabbitMqSubscriber> StartAsync(
        IConnection connection,
        IWyrdRuntime runtime,
        string queue,
        RabbitMqOptions? options = null,
        ILogger<RabbitMqSubscriber>? logger = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentException.ThrowIfNullOrWhiteSpace(queue);
        options ??= new RabbitMqOptions();
        ArgumentOutOfRangeException.ThrowIfZero(options.Prefetch, "options.Prefetch");

        var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        await channel.ExchangeDeclareAsync(options.Exchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        await channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        foreach (var contract in runtime.Subscriptions)
        {
            await channel.QueueBindAsync(queue, options.Exchange, contract, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        await channel.BasicQosAsync(prefetchSize: 0, options.Prefetch, global: false, cancellationToken).ConfigureAwait(false);

        var subscriber = new RabbitMqSubscriber(channel, runtime, options, logger ?? (ILogger)NullLogger.Instance);
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += subscriber.OnReceivedAsync;
        subscriber._consumerTag = await channel.BasicConsumeAsync(queue, autoAck: false, consumer, cancellationToken).ConfigureAwait(false);
        return subscriber;
    }

    private async Task OnReceivedAsync(object sender, BasicDeliverEventArgs delivery)
    {
        var properties = delivery.BasicProperties;
        if (!Guid.TryParse(properties.MessageId, out var id) || string.IsNullOrEmpty(properties.Type))
        {
            LogRejected(delivery.RoutingKey);
            await _channel.BasicRejectAsync(delivery.DeliveryTag, requeue: false, CancellationToken.None).ConfigureAwait(false);
            return;
        }

        var message = TraceHeaders.On(new TransportMessage(id, properties.Type, Encoding.UTF8.GetString(delivery.Body.Span)), properties.Headers);
        try
        {
            await _runtime.ReceiveAsync(message, delivery.CancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is JsonException or NotAnIntegrationEventException)
        {
            // It will never be read, however often it comes: rejected, to the queue's dead-letter exchange if it has one.
            LogUnreadable(message.Type, id, ex);
            await _channel.BasicRejectAsync(delivery.DeliveryTag, requeue: false, CancellationToken.None).ConfigureAwait(false);
            return;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The runtime couldn't take it (its database is down?): hand it back, a little later, so it isn't lost.
            LogReceiveFailed(message.Type, id, ex);
            await Task.Delay(_options.RetryDelay, CancellationToken.None).ConfigureAwait(false);
            await _channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true, CancellationToken.None).ConfigureAwait(false);
            return;
        }

        await _channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>Stops consuming and closes the channel; unacknowledged messages go back to the queue.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_consumerTag is not null && _channel.IsOpen)
        {
            await _channel.BasicCancelAsync(_consumerTag).ConfigureAwait(false);
        }

        await _channel.CloseAsync().ConfigureAwait(false);
        _channel.Dispose();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rejected a message routed as {RoutingKey}: not a Wyrd message (no message id or type)")]
    private partial void LogRejected(string routingKey);

    [LoggerMessage(Level = LogLevel.Error, Message = "{Type} ({Id}) can't be read: rejected, not handed back")]
    private partial void LogUnreadable(string type, Guid id, Exception error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't deliver {Type} ({Id}) to the runtime: handed back to the queue")]
    private partial void LogReceiveFailed(string type, Guid id, Exception error);
}
