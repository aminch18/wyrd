using Wyrd.Processes;
using Wyrd.Runtime;

namespace Wyrd.Tests;

public class TransportTests
{
    private static readonly Process ShipWhenPaid = Process.StartOn<PaymentChargedV1>(e => e.OrderId).Then(Shipping.ShipPaid);

    private static InMemoryRuntime PaymentsService() =>
        new InMemoryRuntime().AddSlice(new ChargePaymentSlice()).AddPublication(new PaymentChargedV1Publication());

    private static InMemoryRuntime ShippingService() => new InMemoryRuntime().AddSlice(new ShipOrderSlice()).AddProcess(ShipWhenPaid);

    [Fact]
    public async Task an_integration_event_reaches_another_service()
    {
        var transport = new InMemoryTransport();
        var payments = PaymentsService().UseTransport(transport);
        var shipping = ShippingService();
        transport.Subscribe(shipping);

        await payments.SendAsync(new ChargePayment("A-1", 50));

        Assert.Equal(new OrderShipped("A-1"), Assert.Single(shipping.History.OfType<OrderShipped>()));
        Assert.Single(transport.Sent);
    }

    [Fact]
    public async Task a_message_delivered_twice_is_handled_once()
    {
        var transport = new InMemoryTransport();
        var payments = PaymentsService().UseTransport(transport);
        var shipping = ShippingService();
        transport.Subscribe(shipping);
        await payments.SendAsync(new ChargePayment("A-1", 50));

        await shipping.ReceiveAsync(transport.Sent[0]);

        Assert.Single(shipping.History.OfType<OrderShipped>());
    }

    [Fact]
    public async Task messages_from_different_services_never_collide()
    {
        var transport = new InMemoryTransport();
        var shipping = ShippingService();
        transport.Subscribe(shipping);

        await PaymentsService().UseTransport(transport).SendAsync(new ChargePayment("A-1", 50));
        await PaymentsService().UseTransport(transport).SendAsync(new ChargePayment("B-1", 20));

        Assert.Equal(2, shipping.History.OfType<OrderShipped>().Count());
    }

    [Fact]
    public async Task a_service_subscribed_to_its_own_messages_handles_them_once()
    {
        var transport = new InMemoryTransport();
        var shop = ShippingService().AddSlice(new ChargePaymentSlice()).AddPublication(new PaymentChargedV1Publication()).UseTransport(transport);
        transport.Subscribe(shop);

        await shop.SendAsync(new ChargePayment("A-1", 50));

        Assert.Single(shop.History.OfType<OrderShipped>());
    }

    [Fact]
    public void a_runtime_subscribes_to_the_contracts_it_uses_not_to_those_it_publishes()
    {
        var shop = ShippingService().AddSlice(new ChargePaymentSlice()).AddPublication(new PaymentChargedV1Publication());

        Assert.Equal(["payments.payment-charged v1"], ShippingService().Subscriptions);
        Assert.Equal(["payments.payment-charged v1"], shop.Subscriptions); // it also publishes it: still used here
        Assert.Empty(PaymentsService().Subscriptions);
    }

    [Fact]
    public async Task a_contract_nobody_uses_is_ignored()
    {
        var transport = new InMemoryTransport();
        var payments = PaymentsService().UseTransport(transport);
        var stock = new InMemoryRuntime().AddSlice(new ReserveStockSlice());
        transport.Subscribe(stock);

        await payments.SendAsync(new ChargePayment("A-1", 50));

        Assert.Empty(stock.History);
        Assert.Contains(stock.Journal, e => e.Message == "payments.payment-charged v1 is not used here, ignored");
    }

    [Fact]
    public async Task only_integration_events_cross_service_boundaries()
    {
        var shipping = ShippingService();
        var message = new TransportMessage(Guid.NewGuid(), MessageTypes.NameOf(typeof(OrderShipped)), """{"OrderId":"A-1"}""");

        var ex = await Assert.ThrowsAsync<NotAnIntegrationEventException>(() => shipping.ReceiveAsync(message));

        Assert.Contains("only integration events cross service boundaries", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task runtimes_are_connected_before_they_handle_messages()
    {
        var payments = PaymentsService();
        await payments.SendAsync(new ChargePayment("A-1", 50));

        var ex = Assert.Throws<InvalidOperationException>(() => payments.UseTransport(new InMemoryTransport()));

        Assert.Equal("Connect a runtime to one in-memory transport, before it handles messages.", ex.Message);
    }

    [Fact]
    public async Task what_a_failing_transport_did_not_take_is_sent_next_time()
    {
        var transport = new FlakyTransport();
        var payments = PaymentsService().UseTransport(transport);

        await Assert.ThrowsAsync<IOException>(() => payments.SendAsync(new ChargePayment("A-1", 50)));
        await payments.SendAsync(new ChargePayment("B-1", 20));

        Assert.Equal(2, transport.Sent.Count);
        Assert.Equal(2, transport.Sent.Select(m => m.MessageId).Distinct().Count());
    }

    private sealed class FlakyTransport : IWyrdTransport
    {
        private bool _failed;

        public List<TransportMessage> Sent { get; } = [];

        public Task SendAsync(TransportMessage message, CancellationToken cancellationToken = default)
        {
            if (!_failed)
            {
                _failed = true;
                throw new IOException("broker unreachable");
            }

            Sent.Add(message);
            return Task.CompletedTask;
        }
    }
}
