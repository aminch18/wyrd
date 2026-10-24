using System.Text;
using RabbitMQ.Client;
using Wyrd.Runtime;

namespace Wyrd.RabbitMq;

/// <summary>
/// Sends what a service publishes to RabbitMQ: each integration event goes to the exchange with its contract as the
/// routing key, persistent, with its message id and trace context, and counts as sent once the broker confirms it. Give
/// it to a runtime with <c>UseTransport</c>: the outbox relay calls it, so a message the broker didn't confirm stays in
/// the outbox and is sent again. A contract no service subscribes to is dropped by the broker, like an event nobody
/// listens to.
/// </summary>
public sealed class RabbitMqTransport : IWyrdTransport, IAsyncDisposable
{
    private readonly IChannel _channel;
    private readonly string _exchange;
    private readonly SemaphoreSlim _publish = new(1, 1);

    private RabbitMqTransport(IChannel channel, string exchange)
    {
        _channel = channel;
        _exchange = exchange;
    }

    /// <summary>
    /// Opens a channel with publisher confirms on <paramref name="connection"/> (yours: the transport doesn't close it)
    /// and declares the exchange.
    /// </summary>
    public static async Task<RabbitMqTransport> CreateAsync(
        IConnection connection, RabbitMqOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        options ??= new RabbitMqOptions();
        var channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            cancellationToken).ConfigureAwait(false);
        await channel.ExchangeDeclareAsync(options.Exchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return new RabbitMqTransport(channel, options.Exchange);
    }

    /// <inheritdoc />
    public async Task SendAsync(TransportMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        var properties = new BasicProperties
        {
            MessageId = message.MessageId.ToString(),
            Type = message.Type,
            ContentType = "application/json",
            Persistent = true,
            Headers = TraceHeaders.Of(message),
        };

        // One publish at a time on the channel: confirms are tracked per channel.
        await _publish.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _channel.BasicPublishAsync(_exchange, message.Type, mandatory: false, properties, Encoding.UTF8.GetBytes(message.Payload), cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _publish.Release();
        }
    }

    /// <summary>Closes the channel; the connection stays open.</summary>
    public async ValueTask DisposeAsync()
    {
        await _channel.CloseAsync().ConfigureAwait(false);
        _channel.Dispose();
        _publish.Dispose();
    }
}
