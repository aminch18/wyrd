using Wyrd.Processes;
using Wyrd.Runtime;
using Wyrd.Testing;

namespace Wyrd.Tests;

/// <summary>A payment provider that, like real ones, deduplicates by idempotency key.</summary>
public sealed class FakeGateway : Integration<ChargePayment, PaymentCharged, PaymentError>
{
    private readonly Dictionary<Guid, PaymentCharged> _charges = [];

    public List<IntegrationContext> Calls { get; } = [];

    /// <summary>Calls that throw before reaching the provider (a network failure).</summary>
    public int FailNext { get; set; }

    /// <summary>Calls that time out on the client side, as <c>HttpClient</c> does: a cancellation nobody asked for.</summary>
    public int TimeOutNext { get; set; }

    /// <summary>Calls that charge, then time out before the answer comes back: the provider acted, Wyrd can't know.</summary>
    public int ChargeThenTimeOutNext { get; set; }

    /// <summary>Charges made and not refunded, by order.</summary>
    public IEnumerable<string> Charged => _charges.Values.Select(c => c.OrderId);

    /// <summary>A refund by order, as a provider would: refunding what was never charged is nothing to do, not an error.</summary>
    public void Refund(string orderId)
    {
        foreach (var key in _charges.Where(c => c.Value.OrderId == orderId).Select(c => c.Key).ToList())
        {
            _charges.Remove(key);
        }
    }

    /// <summary>Distinct charges actually made, whatever the number of calls.</summary>
    public int Charges => _charges.Count;

    public override object KeyOf(ChargePayment command) => command.OrderId;

    public override Task<Result<PaymentCharged, PaymentError>> ExecuteAsync(
        ChargePayment command, IntegrationContext context, CancellationToken cancellationToken)
    {
        Calls.Add(context);
        if (FailNext > 0)
        {
            FailNext--;
            throw new HttpRequestException("connection reset");
        }

        if (TimeOutNext > 0)
        {
            TimeOutNext--;
            throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing.");
        }

        if (command.Amount > 1000)
        {
            return Task.FromResult<Result<PaymentCharged, PaymentError>>(PaymentError.Declined);
        }

        if (!_charges.TryGetValue(context.IdempotencyKey, out var charged))
        {
            charged = new PaymentCharged(command.OrderId, command.Amount);
            _charges[context.IdempotencyKey] = charged;
        }

        if (ChargeThenTimeOutNext > 0)
        {
            ChargeThenTimeOutNext--;
            throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing.");
        }

        return Task.FromResult<Result<PaymentCharged, PaymentError>>(charged);
    }
}

/// <summary>The provider's refund, an integration too: idempotent, and done when there is nothing to refund.</summary>
public sealed class FakeRefunds(FakeGateway gateway) : Integration<RefundPayment, PaymentRefunded, PaymentError>
{
    public override object KeyOf(RefundPayment command) => command.OrderId;

    public override Task<Result<PaymentRefunded, PaymentError>> ExecuteAsync(
        RefundPayment command, IntegrationContext context, CancellationToken cancellationToken)
    {
        gateway.Refund(command.OrderId);
        return Task.FromResult<Result<PaymentRefunded, PaymentError>>(new PaymentRefunded(command.OrderId));
    }
}

public class IntegrationTests
{
    private static readonly Process Fulfillment = FluentProcess.OrderFulfillment;

    [Fact]
    public async Task a_step_owned_by_an_integration_runs_like_any_other_step()
    {
        var gateway = new FakeGateway();
        var runtime = TestSystem.WithGateway(gateway, Fulfillment);

        await runtime.SendAsync(new PlaceOrder("A-1", Quantity: 1, Amount: 50));
        await runtime.PublishAsync(new PaymentConfirmed("A-1"));

        Assert.Equal(ProcessStatus.Completed, runtime.StatusOf(Fulfillment, "A-1"));
        Assert.Contains(new PaymentCharged("A-1", 50), runtime.History);
        Assert.Single(runtime.Published.OfType<PaymentChargedV1>()); // publications apply to its events too
    }

    [Fact]
    public async Task a_third_party_timeout_is_a_failure_the_process_retries()
    {
        var gateway = new FakeGateway { TimeOutNext = 1 };
        var runtime = TestSystem.WithGateway(gateway, Fulfillment);

        await runtime.SendAsync(new PlaceOrder("A-1", Quantity: 1, Amount: 50));
        await runtime.AdvanceAsync(1.Minutes);

        Assert.Equal(2, gateway.Calls.Count);
        Assert.Equal(1, gateway.Charges);
        Assert.Equal(ProcessStatus.Awaiting, runtime.StatusOf(Fulfillment, "A-1"));
    }

    [Fact]
    public async Task a_call_that_never_answered_is_undone_in_case_it_acted()
    {
        var gateway = new FakeGateway { TimeOutNext = 10 }; // every attempt times out: did the provider charge? Unknown.
        var runtime = TestSystem.WithGateway(gateway, Fulfillment);

        await runtime.SendAsync(new PlaceOrder("A-1", Quantity: 1, Amount: 50));
        await runtime.AdvanceAsync(1.Hours); // retries exhausted: suspended
        await runtime.CompensateAsync(Fulfillment, "A-1");

        // Not "never ran, fenced": the refund is asked for. This shop's refund finds no charge it knows of and refuses,
        // so the instance waits for an operator again instead of compensating as if nothing could have happened.
        Assert.Contains(runtime.Journal, e => e.Message == "RefundPayment: the original call's outcome is unknown, fenced and undone");
        Assert.DoesNotContain(runtime.Journal, e => e.Message.EndsWith("the original command never ran, fenced", StringComparison.Ordinal));
        Assert.Equal(ProcessStatus.Suspended, runtime.StatusOf(Fulfillment, "A-1"));
    }

    [Fact]
    public async Task a_charge_that_timed_out_after_the_provider_charged_is_refunded_on_compensation()
    {
        var gateway = new FakeGateway { ChargeThenTimeOutNext = 10 }; // charged on the first call; no answer ever came back
        var runtime = new InMemoryRuntime()
            .AddIntegration(gateway)
            .AddIntegration(new FakeRefunds(gateway))
            .AddSlice(new PlaceOrderSlice())
            .AddSlice(new ReserveStockSlice())
            .AddSlice(new ReleaseStockSlice())
            .AddSlice(new ShipOrderSlice())
            .AddSlice(new CancelOrderSlice())
            .AddProcess(Fulfillment);

        await runtime.SendAsync(new PlaceOrder("A-1", Quantity: 1, Amount: 50));
        await runtime.AdvanceAsync(1.Hours); // retries exhausted: suspended
        Assert.Equal(["A-1"], gateway.Charged);

        await runtime.CompensateAsync(Fulfillment, "A-1");

        Assert.Empty(gateway.Charged); // refunded, though Wyrd never saw the charge succeed
        Assert.Equal(ProcessStatus.Compensated, runtime.StatusOf(Fulfillment, "A-1"));
    }

    [Fact]
    public async Task a_failure_is_retried_with_the_same_idempotency_key()
    {
        var gateway = new FakeGateway { FailNext = 2 };
        var runtime = TestSystem.WithGateway(gateway, Fulfillment);

        await runtime.SendAsync(new PlaceOrder("A-1", Quantity: 1, Amount: 50));
        await runtime.AdvanceAsync(1.Minutes);

        Assert.Equal(3, gateway.Calls.Count);
        Assert.Single(gateway.Calls.Select(c => c.IdempotencyKey).Distinct());
        Assert.Equal([1, 2, 3], gateway.Calls.Select(c => c.Attempt));
        Assert.Equal(1, gateway.Charges);
        Assert.Contains(runtime.Journal, e => e.Message == "ChargePayment failed: HttpRequestException: connection reset");
    }

    [Fact]
    public async Task a_business_error_is_a_rejection_and_compensates()
    {
        var gateway = new FakeGateway();
        var runtime = TestSystem.WithGateway(gateway, Fulfillment);

        await runtime.SendAsync(new PlaceOrder("A-1", Quantity: 1, Amount: 5_000));

        Assert.Equal(ProcessStatus.Compensated, runtime.StatusOf(Fulfillment, "A-1"));
        Assert.Single(gateway.Calls); // never retried
    }

    [Fact]
    public void a_command_has_one_owner_slice_or_integration()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new InMemoryRuntime().AddSlice(new ChargePaymentSlice()).AddIntegration(new FakeGateway()));

        Assert.Contains("ChargePayment is already owned by ChargePaymentSlice", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task sent_directly_an_integration_answers_and_its_failure_reaches_the_caller()
    {
        var gateway = new FakeGateway();
        var runtime = TestSystem.WithGateway(gateway);

        Assert.IsType<Decision.Accepted>(await runtime.SendAsync(new ChargePayment("A-1", 50)));

        gateway.FailNext = 1;
        await Assert.ThrowsAsync<HttpRequestException>(() => runtime.SendAsync(new ChargePayment("A-2", 50)));
    }

    [Fact]
    public async Task urd_replays_an_integration_from_the_recording_without_calling_it()
    {
        var recorded = TestSystem.WithGateway(new FakeGateway(), Fulfillment);
        await recorded.SendAsync(new PlaceOrder("A-1", Quantity: 1, Amount: 50));
        var history = recorded.HistoryOf(Fulfillment, "A-1");

        var gateway = new FakeGateway();
        var report = await Urd.ReplayAsync(() => TestSystem.WithGateway(gateway, Fulfillment), Fulfillment, history);

        report.ThrowIfDiverged();
        Assert.Empty(gateway.Calls);
    }

    [Fact]
    public async Task skuld_finds_no_future_where_a_card_is_charged_twice()
    {
        FakeGateway gateway = null!;
        var report = await Skuld.SimulateAsync(
            () => TestSystem.WithGateway(gateway = new FakeGateway(), Fulfillment),
            async shop =>
            {
                await shop.SendAsync(new PlaceOrder("A-1", Quantity: 1, Amount: 50));
                await shop.SendAsync(new PlaceOrder("A-2", Quantity: 1, Amount: 70));
            },
            [new Invariant("a card is charged at most once per order", _ =>
                gateway.Charges <= 2 ? null : $"{gateway.Charges} charges for 2 orders")],
            new SimulationOptions { Runs = 500, FaultRate = 0.3, DuplicateRate = 0.3 });

        report.ThrowIfViolated();
    }
    [Fact]
    public async Task reading_never_waits_for_an_operation_in_progress()
    {
        var gateway = new SlowGateway();
        var runtime = TestSystem.WithGateway(gateway, Fulfillment);
        var placing = runtime.SendAsync(new PlaceOrder("A-1", Quantity: 1, Amount: 50));
        await gateway.Called.Task.WaitAsync(TimeSpan.FromSeconds(5)); // the operation is now inside the gateway call

        var reading = Task.Run(() => runtime.StatusOf(Fulfillment, "A-1"));

        Assert.Same(reading, await Task.WhenAny(reading, Task.Delay(TimeSpan.FromSeconds(5))));
        Assert.Equal(ProcessStatus.Running, await reading);
        gateway.Answer.SetResult();
        await placing;
    }

    /// <summary>A payment provider that answers only when told to.</summary>
    private sealed class SlowGateway : Integration<ChargePayment, PaymentCharged, PaymentError>
    {
        public TaskCompletionSource Called { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Answer { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override object KeyOf(ChargePayment command) => command.OrderId;

        public override async Task<Result<PaymentCharged, PaymentError>> ExecuteAsync(
            ChargePayment command, IntegrationContext context, CancellationToken cancellationToken)
        {
            Called.TrySetResult();
            await Answer.Task.WaitAsync(cancellationToken);
            return new PaymentCharged(command.OrderId, command.Amount);
        }
    }
}
