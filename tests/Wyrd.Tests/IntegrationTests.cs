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

        if (command.Amount > 1000)
        {
            return Task.FromResult<Result<PaymentCharged, PaymentError>>(PaymentError.Declined);
        }

        if (!_charges.TryGetValue(context.IdempotencyKey, out var charged))
        {
            charged = new PaymentCharged(command.OrderId, command.Amount);
            _charges[context.IdempotencyKey] = charged;
        }

        return Task.FromResult<Result<PaymentCharged, PaymentError>>(charged);
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
}
