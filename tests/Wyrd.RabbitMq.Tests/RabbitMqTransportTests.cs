using RabbitMQ.Client;
using Shop;
using Shop.Contracts;
using Shop.Inventory;
using Shop.Orders;
using Testcontainers.RabbitMq;
using Wyrd.Runtime;

namespace Wyrd.RabbitMq.Tests;

/// <summary>One broker for the whole run; every test gets an exchange of its own.</summary>
public sealed class Broker : IAsyncLifetime
{
    private readonly RabbitMqContainer _container = new RabbitMqBuilder("rabbitmq:4-alpine").Build();
    private int _exchanges;

    public IConnection Connection { get; private set; } = null!;

    public RabbitMqOptions NewOptions() => new() { Exchange = $"wyrd-{Interlocked.Increment(ref _exchanges)}", RetryDelay = TimeSpan.FromMilliseconds(50) };

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        Connection = await new ConnectionFactory { Uri = new Uri(_container.GetConnectionString()) }.CreateConnectionAsync();
    }

    public async Task DisposeAsync()
    {
        await Connection.DisposeAsync();
        await _container.DisposeAsync();
    }
}

public sealed record Shipment(string OrderId, int Deliveries);

/// <summary>The shipping side of another service: counts every delivery it handles, so a duplicate shows.</summary>
public sealed class Shipments : Projection<Shipment>
{
    public Shipments() =>
        On<OrderShippedV1>(e => e.OrderId, (view, e) => new Shipment(e.OrderId, (view?.Deliveries ?? 0) + 1));
}

[Trait("Category", "Containers")]
public sealed class RabbitMqTransportTests(Broker broker) : IClassFixture<Broker>
{
    [Fact]
    public async Task what_one_service_publishes_reaches_another_through_rabbitmq()
    {
        var options = broker.NewOptions();
        using var shipping = new InMemoryRuntime().AddProjection(new Shipments());
        await using var subscriber = await RabbitMqSubscriber.StartAsync(broker.Connection, shipping, Queue(), options);
        await using var transport = await RabbitMqTransport.CreateAsync(broker.Connection, options);
        using var shop = ShopModule.CreateRuntime().UseTransport(transport);

        await shop.SendAsync(new Restock("KEYBOARD", 5));
        await shop.SendAsync(new PlaceOrder("A-1", "KEYBOARD", 1, 89m));
        await shop.PublishAsync(new PaymentConfirmed("A-1"));

        Assert.Equal(new Shipment("A-1", 1), await UntilAsync(() => shipping.View<Shipment>("A-1")));
    }

    [Fact]
    public async Task a_message_the_broker_delivers_twice_is_handled_once()
    {
        var options = broker.NewOptions();
        using var shipping = new InMemoryRuntime().AddProjection(new Shipments());
        await using var subscriber = await RabbitMqSubscriber.StartAsync(broker.Connection, shipping, Queue(), options);
        await using var transport = await RabbitMqTransport.CreateAsync(broker.Connection, options);

        var shipped = Message("A-1");
        await transport.SendAsync(shipped);
        await transport.SendAsync(shipped);
        await transport.SendAsync(Message("B-2")); // one consumer, in order: once B-2 is in, both copies of A-1 were handled

        await UntilAsync(() => shipping.View<Shipment>("B-2"));
        Assert.Equal(new Shipment("A-1", 1), shipping.View<Shipment>("A-1"));
    }

    [Fact]
    public async Task a_service_receives_only_the_contracts_it_uses()
    {
        var options = broker.NewOptions();
        using var shipping = new InMemoryRuntime().AddProjection(new Shipments());
        await using var subscriber = await RabbitMqSubscriber.StartAsync(broker.Connection, shipping, Queue(), options);
        await using var transport = await RabbitMqTransport.CreateAsync(broker.Connection, options);

        await transport.SendAsync(new TransportMessage(Guid.NewGuid(), "payments.payment-charged v1", """{"OrderId":"A-1","Amount":1,"Currency":"EUR"}"""));
        await transport.SendAsync(Message("B-2"));

        await UntilAsync(() => shipping.View<Shipment>("B-2"));
        Assert.Equal(["shipping.order-shipped v1"], shipping.Subscriptions);
        Assert.DoesNotContain(shipping.Journal, e => e.Message.EndsWith("is not used here, ignored", StringComparison.Ordinal));
    }

    [Fact]
    public async Task the_trace_context_crosses_with_the_message()
    {
        var options = broker.NewOptions();
        var received = new TaskCompletionSource<TransportMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var subscriber = await RabbitMqSubscriber.StartAsync(broker.Connection, new Recorder(received), Queue(), options);
        await using var transport = await RabbitMqTransport.CreateAsync(broker.Connection, options);
        var sent = Message("A-1") with { TraceParent = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01", TraceState = "wyrd=1" };

        await transport.SendAsync(sent);

        Assert.Equal(sent, await received.Task.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task a_message_that_can_never_be_read_is_rejected_not_redelivered_forever()
    {
        var options = broker.NewOptions();
        using var shipping = new InMemoryRuntime().AddProjection(new Shipments());
        var queue = Queue();
        await using var subscriber = await RabbitMqSubscriber.StartAsync(broker.Connection, shipping, queue, options);
        await using var transport = await RabbitMqTransport.CreateAsync(broker.Connection, options);

        await transport.SendAsync(new TransportMessage(Guid.NewGuid(), "shipping.order-shipped v1", "not json"));
        await transport.SendAsync(Message("B-2")); // one consumer, in order: the next message gets through

        await UntilAsync(() => shipping.View<Shipment>("B-2"));
        await using var channel = await broker.Connection.CreateChannelAsync();
        Assert.Equal(0u, await channel.MessageCountAsync(queue));
    }

    [Fact]
    public async Task a_subscriber_without_a_prefetch_limit_is_refused()
    {
        using var shipping = new InMemoryRuntime().AddProjection(new Shipments());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            RabbitMqSubscriber.StartAsync(broker.Connection, shipping, Queue(), broker.NewOptions() with { Prefetch = 0 }));
    }

    private static string Queue() => $"shipping-{Guid.NewGuid():N}";

    private static TransportMessage Message(string orderId) =>
        new(Guid.NewGuid(), "shipping.order-shipped v1", $$"""{"OrderId":"{{orderId}}","ShippedOn":"2026-01-01"}""");

    private static async Task<T> UntilAsync<T>(Func<T?> read)
        where T : class
    {
        var timeout = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        for (var value = read(); ; value = read())
        {
            if (value is not null)
            {
                return value;
            }

            Assert.True(DateTime.UtcNow < timeout, "Nothing arrived through the broker.");
            await Task.Delay(20);
        }
    }

    /// <summary>A runtime that only records what it receives.</summary>
    private sealed class Recorder(TaskCompletionSource<TransportMessage> received) : IWyrdRuntime
    {
        public IReadOnlyList<string> Subscriptions => ["shipping.order-shipped v1"];

        public Task ReceiveAsync(TransportMessage message, CancellationToken cancellationToken = default)
        {
            received.TrySetResult(message);
            return Task.CompletedTask;
        }

        public Task<Decision> SendAsync(ICommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task PublishAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ProcessStatus?> StatusOfAsync(Processes.Process process, object key, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task ResumeAsync(Processes.Process process, object key, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task CompensateAsync(Processes.Process process, object key, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<InstanceHistory?> HistoryOfAsync(Processes.Process process, object key, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<TState> StateOfAsync<TState>(object key, CancellationToken cancellationToken = default)
            where TState : IState<TState> => throw new NotSupportedException();

        public Task<TView?> ViewAsync<TView>(object key, CancellationToken cancellationToken = default)
            where TView : class => throw new NotSupportedException();

        public Task<IReadOnlyList<TView>> ViewsAsync<TView>(CancellationToken cancellationToken = default)
            where TView : class => throw new NotSupportedException();
    }
}
