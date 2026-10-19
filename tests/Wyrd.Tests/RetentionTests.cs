using Wyrd.Processes;
using Wyrd.Runtime;

namespace Wyrd.Tests;

public class RetentionTests
{
    private static readonly Process Fulfillment = FluentProcess.OrderFulfillment;

    private static readonly Retention Short = new()
    {
        Deduplication = Keep.For(1.Hours),
        FinishedInstances = Keep.For(1.Hours),
        RelayedMessages = Keep.For(1.Hours),
        Events = Keep.For(1.Hours),
        DeadLetters = Keep.For(1.Hours),
    };

    private readonly InMemoryRuntime _runtime = TestSystem.Create(Fulfillment).UseRetention(Short);

    [Fact]
    public async Task a_finished_instance_is_forgotten_after_its_retention()
    {
        await CompleteAsync("A-1");

        await _runtime.AdvanceAsync(59.Minutes);
        Assert.Equal(ProcessStatus.Completed, _runtime.StatusOf(Fulfillment, "A-1"));

        await _runtime.AdvanceAsync(1.Minutes);
        Assert.Null(_runtime.StatusOf(Fulfillment, "A-1"));

        // "Once per order" is the domain's rule, and it still holds: the order already exists.
        Assert.IsType<Decision.Rejected>(await _runtime.SendAsync(new PlaceOrder("A-1", Quantity: 1, Amount: 50)));
        Assert.Null(_runtime.StatusOf(Fulfillment, "A-1"));
    }

    [Fact]
    public async Task once_its_instance_is_forgotten_a_key_can_start_a_new_one()
    {
        var shipWhenPaid = Process.StartOn<PaymentChargedV1>(e => e.OrderId).Then(Shipping.ShipPaid);
        var runtime = new InMemoryRuntime().AddSlice(new ShipOrderSlice()).AddProcess(shipWhenPaid).UseRetention(Short);
        await runtime.PublishAsync(new PaymentChargedV1("A-1", 50));
        await runtime.PublishAsync(new PaymentChargedV1("A-1", 50));
        Assert.Contains(runtime.Journal, e => e.Message == "duplicate PaymentChargedV1 ignored");

        await runtime.AdvanceAsync(1.Hours);
        await runtime.PublishAsync(new PaymentChargedV1("A-1", 50));

        Assert.Equal(ProcessStatus.Completed, runtime.StatusOf(shipWhenPaid, "A-1"));
        Assert.Equal(2, runtime.Journal.Count(e => e.Message == "started by PaymentChargedV1"));
    }

    [Fact]
    public async Task a_suspended_instance_never_expires()
    {
        _runtime.FailNext<ChargePayment>(times: 4); // one attempt + Retry(3.Times)
        await _runtime.SendAsync(new PlaceOrder("A-1", Quantity: 1, Amount: 50));
        await _runtime.AdvanceAsync(10.Seconds);

        await _runtime.AdvanceAsync(30.Days);

        Assert.Equal(ProcessStatus.Suspended, _runtime.StatusOf(Fulfillment, "A-1"));
    }

    [Fact]
    public async Task a_duplicate_is_recognised_only_within_the_deduplication_window()
    {
        var transport = new InMemoryTransport();
        var payments = new InMemoryRuntime().AddSlice(new ChargePaymentSlice()).AddPublication(new PaymentChargedV1Publication()).UseTransport(transport);
        var shipping = new InMemoryRuntime().AddSlice(new ShipOrderSlice())
            .AddProcess(Process.StartOn<PaymentChargedV1>(e => e.OrderId).Then(Shipping.ShipPaid))
            .UseRetention(Short with { FinishedInstances = Keep.Forever("this test is about deduplication") });
        transport.Subscribe(shipping);
        await payments.SendAsync(new ChargePayment("A-1", 50));
        var message = Assert.Single(transport.Sent);

        await shipping.AdvanceAsync(30.Minutes);
        await shipping.ReceiveAsync(message);
        Assert.DoesNotContain(shipping.Journal, e => e.Message == "duplicate PaymentChargedV1 ignored");

        await shipping.AdvanceAsync(31.Minutes);
        await shipping.ReceiveAsync(message);
        Assert.Contains(shipping.Journal, e => e.Message == "duplicate PaymentChargedV1 ignored"); // only the instance stops it now
    }

    [Fact]
    public async Task events_and_published_messages_expire()
    {
        await CompleteAsync("A-1");
        Assert.NotEmpty(_runtime.History);
        Assert.NotEmpty(_runtime.Published);

        await _runtime.AdvanceAsync(1.Hours);

        Assert.Empty(_runtime.History);
        Assert.Empty(_runtime.Published);
    }

    [Fact]
    public async Task what_is_kept_forever_stays()
    {
        var runtime = TestSystem.Create(Fulfillment); // the in-memory default keeps everything
        await runtime.SendAsync(new PlaceOrder("A-1", Quantity: 1, Amount: 50));
        await runtime.PublishAsync(new PaymentConfirmed("A-1"));

        await runtime.AdvanceAsync(3_650.Days);

        Assert.Equal(ProcessStatus.Completed, runtime.StatusOf(Fulfillment, "A-1"));
        Assert.NotEmpty(runtime.History);
    }

    [Fact]
    public async Task the_journal_keeps_the_most_recent_entries()
    {
        _runtime.JournalLimit = 20;

        for (var order = 1; order <= 20; order++)
        {
            await _runtime.SendAsync(new PlaceOrder($"A-{order}", Quantity: 1, Amount: 50));
        }

        Assert.InRange(_runtime.Journal.Count, 20, 22);
        Assert.Contains(_runtime.Journal, e => Equals(e.Key, "A-20"));
        Assert.DoesNotContain(_runtime.Journal, e => Equals(e.Key, "A-1"));
    }

    [Fact]
    public void retention_is_a_decision_with_a_reason_when_it_is_forever()
    {
        Assert.Throws<ArgumentException>(() => Keep.Forever(" "));
        Assert.Throws<ArgumentOutOfRangeException>(() => Keep.For(TimeSpan.Zero));
        Assert.Equal("for 7d", Keep.For(7.Days).ToString());
        Assert.Equal("forever (audit)", Keep.Forever("audit").ToString());
    }

    private async Task CompleteAsync(string orderId)
    {
        await _runtime.SendAsync(new PlaceOrder(orderId, Quantity: 1, Amount: 50));
        await _runtime.PublishAsync(new PaymentConfirmed(orderId));
        Assert.Equal(ProcessStatus.Completed, _runtime.StatusOf(Fulfillment, orderId));
    }
}
