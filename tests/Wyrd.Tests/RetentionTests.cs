using System.Diagnostics.CodeAnalysis;
using Wyrd.Processes;
using Wyrd.Runtime;

namespace Wyrd.Tests;

public class RetentionTests
{
    private static readonly Process Fulfillment = FluentProcess.OrderFulfillment;

    // Same name, a step inserted: the instance can't continue with it.
    private static readonly Process Reshaped = Process.StartOn<OrderPlaced>(e => e.OrderId, Fulfillment.Name)
        .Then(Stock.Reserve).Retry(3.Times).Undo(Stock.Release)
        .Then(Payment.Charge).Retry(3.Times).Undo(Payment.Refund)
        .Then(Payment.Charge).NoRetry("only here to change the shape")
        .Await<PaymentConfirmed>(e => e.OrderId).Within(15.Minutes)
        .Then(Shipping.Ship);

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
    public async Task a_fence_outlives_the_deduplication_window()
    {
        var now = DateTimeOffset.UnixEpoch;
        var store = new InMemoryStore(() => now);
        Guid fenced = Guid.NewGuid(), answered = Guid.NewGuid();
        var changes = new Changes();
        changes.Replies[fenced] = new ReplyMessage(ReplyOutcome.Fenced);
        changes.Replies[answered] = new ReplyMessage(ReplyOutcome.Rejected);
        await store.CommitAsync(changes, CancellationToken.None);

        now += 365.Days;
        store.Forget(Short, relays: false);

        // A late copy of a fenced command must never run, however late it is (semantics 3.4).
        Assert.Equal(ReplyOutcome.Fenced, (await store.FindReplyAsync(fenced, CancellationToken.None))?.Outcome);
        Assert.Null(await store.FindReplyAsync(answered, CancellationToken.None));
    }

    [Fact]
    public async Task a_reply_kept_while_the_definition_changed_outlives_the_deduplication_window()
    {
        var runtime = await ChargedWhileRedeployedAsync();
        await runtime.AdvanceAsync(1.Days); // the owner's inbox has forgotten the charge

        runtime.Redeploy(Fulfillment);
        await runtime.ResumeAsync(Fulfillment, "A-1");
        await runtime.PublishAsync(new PaymentConfirmed("A-1"));

        Assert.Equal(ProcessStatus.Completed, runtime.StatusOf(Fulfillment, "A-1"));
        Assert.Single(runtime.History.OfType<PaymentCharged>()); // charged once, not again on resume
    }

    [Fact]
    public async Task compensating_with_a_kept_reply_undoes_what_ran_after_the_deduplication_window()
    {
        var runtime = await ChargedWhileRedeployedAsync();
        await runtime.AdvanceAsync(1.Days);

        runtime.Redeploy(Fulfillment);
        await runtime.CompensateAsync(Fulfillment, "A-1");

        // The charge ran, and the instance knows it: it is refunded, not fenced as if it never ran.
        Assert.Equal(ProcessStatus.Compensated, runtime.StatusOf(Fulfillment, "A-1"));
        Assert.Single(runtime.History.OfType<PaymentRefunded>());
    }

    [Fact]
    [RequiresUnreferencedCode("Reflection-based JSON in a test.")]
    [RequiresDynamicCode("Reflection-based JSON in a test.")]
    public async Task a_kept_reply_goes_once_the_same_reply_is_handled_normally()
    {
        var application = new Application(MessageTypes.Reflection());
        foreach (var slice in new ISliceHandler[]
        {
            new PlaceOrderSlice(), new ReserveStockSlice(), new ReleaseStockSlice(), new ChargePaymentSlice(),
            new RefundPaymentSlice(), new ShipOrderSlice(), new CancelOrderSlice(),
        })
        {
            application.Add(slice);
        }

        application.Add(Fulfillment);
        var store = new InMemoryStore(() => DateTimeOffset.UnixEpoch);
        var ids = 0;
        var engine = new Engine(
            application.Registry, store, application.Types, () => DateTimeOffset.UnixEpoch, () => new Guid(++ids, 0, 0, new byte[8]), (_, _) => false, (_, _, _) => { });
        var (type, payload) = application.Types.Serialize(new PlaceOrder("A-1", 1, 50), typeof(PlaceOrder));
        await engine.HandleAsync(new Envelope(Guid.NewGuid(), MessageKind.Command, type, payload), default);
        await DeliverAsync(engine, store); // OrderPlaced: the instance starts and sends ReserveStock
        await DeliverAsync(engine, store); // ReserveStock: its reply (and StockReserved) is queued
        var reserved = Assert.Single(await store.TakeOutboxAsync(default), e => e.Kind == MessageKind.Reply);

        application.Redeploy(Reshaped);
        await engine.HandleAsync(reserved, default);
        Assert.NotNull(store.Instance(Fulfillment.Name, "A-1")!.KeptReply);

        // The definition is back, and the broker delivers the same reply again: handled like any other.
        application.Redeploy(Fulfillment);
        await engine.HandleAsync(reserved, default);

        var instance = store.Instance(Fulfillment.Name, "A-1")!;
        Assert.Equal(ProcessStatus.Running, instance.Status);
        Assert.Null(instance.KeptReply); // else a later operator would take this charge's step as answered
    }

    private static async Task DeliverAsync(Engine engine, InMemoryStore store)
    {
        foreach (var envelope in await store.TakeOutboxAsync(default))
        {
            await engine.HandleAsync(envelope, default);
        }
    }

    /// <summary>The charge's retry succeeds after a redeploy that changed the process: its reply is kept, suspended.</summary>
    private static async Task<InMemoryRuntime> ChargedWhileRedeployedAsync()
    {
        var runtime = TestSystem.Create(Fulfillment).UseRetention(Short with { Events = Keep.Forever("this test counts the charges") });
        runtime.FailNext<ChargePayment>();
        await runtime.SendAsync(new PlaceOrder("A-1", Quantity: 1, Amount: 50)); // the charge is retried in 1 s
        runtime.Redeploy(Reshaped);
        await runtime.AdvanceAsync(1.Seconds);
        Assert.Equal(ProcessStatus.Suspended, runtime.StatusOf(Fulfillment, "A-1"));
        return runtime;
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
