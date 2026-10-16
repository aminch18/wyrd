using Wyrd.Processes;
using Wyrd.Runtime;

namespace Wyrd.Tests;

public class RuntimeTests
{
    private static readonly Process Fulfillment = FluentProcess.OrderFulfillment;

    private readonly InMemoryRuntime _runtime = TestSystem.Create(Fulfillment);

    [Fact]
    public async Task happy_path_waits_for_confirmation_then_completes()
    {
        await _runtime.SendAsync(new PlaceOrder("A-1", Quantity: 1, Amount: 50));
        Assert.Equal(ProcessStatus.Awaiting, _runtime.StatusOf(Fulfillment, "A-1"));

        await _runtime.PublishAsync(new PaymentConfirmed("A-1"));

        Assert.Equal(ProcessStatus.Completed, _runtime.StatusOf(Fulfillment, "A-1"));
        Assert.Equal(
            [new StockReserved("A-1"), new PaymentCharged("A-1", 50), new OrderShipped("A-1")],
            Events());
    }

    [Fact]
    public async Task a_rejected_step_compensates_only_what_completed()
    {
        await _runtime.SendAsync(new PlaceOrder("A-1", Quantity: 1, Amount: 5_000));

        Assert.Equal(ProcessStatus.Compensated, _runtime.StatusOf(Fulfillment, "A-1"));
        Assert.Equal([new StockReserved("A-1"), new StockReleased("A-1")], Events());
    }

    [Fact]
    public async Task a_rejected_first_step_has_nothing_to_compensate()
    {
        await _runtime.SendAsync(new PlaceOrder("A-1", Quantity: 99, Amount: 50));

        Assert.Equal(ProcessStatus.Compensated, _runtime.StatusOf(Fulfillment, "A-1"));
        Assert.Empty(Events());
    }

    [Fact]
    public async Task transient_failures_are_retried_with_backoff()
    {
        _runtime.FailNext<ChargePayment>(times: 2);

        await _runtime.SendAsync(new PlaceOrder("A-1", Quantity: 1, Amount: 50));
        Assert.Equal(ProcessStatus.Running, _runtime.StatusOf(Fulfillment, "A-1"));

        await _runtime.AdvanceAsync(3.Seconds); // retries after 1s, then 2s

        Assert.Equal(ProcessStatus.Awaiting, _runtime.StatusOf(Fulfillment, "A-1"));
        Assert.Equal(
            ["transient failure, retry 1/3 after 1s", "transient failure, retry 2/3 after 2s"],
            _runtime.Journal.Where(e => e.Message.StartsWith("transient", StringComparison.Ordinal)).Select(e => e.Message));
    }

    [Fact]
    public async Task exhausted_retries_compensate()
    {
        _runtime.FailNext<ChargePayment>(times: 4);

        await _runtime.SendAsync(new PlaceOrder("A-1", Quantity: 1, Amount: 50));
        await _runtime.AdvanceAsync(10.Seconds);

        Assert.Equal(ProcessStatus.Compensated, _runtime.StatusOf(Fulfillment, "A-1"));
        Assert.Equal([new StockReserved("A-1"), new StockReleased("A-1")], Events());
    }

    [Fact]
    public async Task a_timeout_compensates_in_reverse_order()
    {
        await _runtime.SendAsync(new PlaceOrder("A-1", Quantity: 1, Amount: 50));

        await _runtime.AdvanceAsync(14.Minutes);
        Assert.Equal(ProcessStatus.Awaiting, _runtime.StatusOf(Fulfillment, "A-1"));

        await _runtime.AdvanceAsync(1.Minutes);
        Assert.Equal(ProcessStatus.Compensated, _runtime.StatusOf(Fulfillment, "A-1"));
        Assert.Equal(
            [new StockReserved("A-1"), new PaymentCharged("A-1", 50), new PaymentRefunded("A-1"), new StockReleased("A-1")],
            Events());
    }

    [Fact]
    public async Task a_confirmation_after_the_timeout_is_ignored()
    {
        await _runtime.SendAsync(new PlaceOrder("A-1", Quantity: 1, Amount: 50));
        await _runtime.AdvanceAsync(15.Minutes);

        await _runtime.PublishAsync(new PaymentConfirmed("A-1"));

        Assert.Equal(ProcessStatus.Compensated, _runtime.StatusOf(Fulfillment, "A-1"));
        Assert.DoesNotContain(new OrderShipped("A-1"), Events());
    }

    [Fact]
    public async Task an_event_that_arrives_before_the_wait_is_parked_and_used_later()
    {
        _runtime.FailNext<ChargePayment>();
        await _runtime.SendAsync(new PlaceOrder("A-1", Quantity: 1, Amount: 50));

        await _runtime.PublishAsync(new PaymentConfirmed("A-1")); // the webhook beats our retry
        await _runtime.AdvanceAsync(1.Seconds);

        Assert.Equal(ProcessStatus.Completed, _runtime.StatusOf(Fulfillment, "A-1"));
        Assert.Contains(_runtime.Journal, e => e.Message == "PaymentConfirmed arrived early, parked");
        Assert.Contains(_runtime.Journal, e => e.Message == "<- PaymentConfirmed (parked)");
    }

    [Fact]
    public async Task an_event_that_arrives_before_the_process_exists_is_parked_too()
    {
        await _runtime.PublishAsync(new PaymentConfirmed("A-1"));

        await _runtime.SendAsync(new PlaceOrder("A-1", Quantity: 1, Amount: 50));

        Assert.Equal(ProcessStatus.Completed, _runtime.StatusOf(Fulfillment, "A-1"));
    }

    [Fact]
    public async Task placing_the_same_order_twice_is_rejected_by_the_decision()
    {
        await _runtime.SendAsync(new PlaceOrder("A-1", Quantity: 1, Amount: 50));
        await _runtime.SendAsync(new PlaceOrder("A-1", Quantity: 1, Amount: 50));

        Assert.Single(Events().OfType<PaymentCharged>());
        Assert.Contains(_runtime.Journal, e => e.Message == "rejected AlreadyExists");
    }

    [Fact]
    public async Task a_redelivered_integration_event_starts_a_single_instance()
    {
        Process shipWhenPaid = Process.StartOn<PaymentChargedV1>(e => e.OrderId).Then(Shipping.ShipPaid);
        var runtime = TestSystem.Create(shipWhenPaid);

        await runtime.PublishAsync(new PaymentChargedV1("A-1", 50));
        await runtime.PublishAsync(new PaymentChargedV1("A-1", 50));

        Assert.Single(runtime.History.OfType<OrderShipped>());
        Assert.Contains(runtime.Journal, e => e.Message == "duplicate PaymentChargedV1 ignored");
    }

    [Fact]
    public async Task confirmations_are_correlated_by_key()
    {
        await _runtime.SendAsync(new PlaceOrder("A-1", Quantity: 1, Amount: 50));
        await _runtime.SendAsync(new PlaceOrder("A-2", Quantity: 1, Amount: 50));

        await _runtime.PublishAsync(new PaymentConfirmed("A-2"));

        Assert.Equal(ProcessStatus.Awaiting, _runtime.StatusOf(Fulfillment, "A-1"));
        Assert.Equal(ProcessStatus.Completed, _runtime.StatusOf(Fulfillment, "A-2"));
    }

    [Fact]
    public async Task a_failed_compensation_needs_attention()
    {
        _runtime.FailNext<ReleaseStock>();

        await _runtime.SendAsync(new PlaceOrder("A-1", Quantity: 1, Amount: 5_000));

        Assert.Equal(ProcessStatus.Failed, _runtime.StatusOf(Fulfillment, "A-1"));
    }

    [Fact]
    public async Task the_same_inputs_produce_the_same_journal()
    {
        static async Task<IReadOnlyList<JournalEntry>> Run()
        {
            var runtime = TestSystem.Create(Fulfillment);
            runtime.FailNext<ChargePayment>();
            await runtime.SendAsync(new PlaceOrder("A-1", Quantity: 1, Amount: 50));
            await runtime.AdvanceAsync(20.Minutes);
            return runtime.Journal;
        }

        Assert.Equal(await Run(), await Run());
    }

    [Fact]
    public async Task commands_can_be_sent_from_outside_a_process()
    {
        var decision = await _runtime.SendAsync(new ChargePayment("B-1", Amount: 5_000));

        Assert.Equal(new Decision.Rejected(PaymentError.Declined), decision);
        Assert.Empty(Events());
    }

    [Fact]
    public async Task state_is_stored_and_evolved_from_accepted_events()
    {
        await _runtime.SendAsync(new ReserveStock("B-1", Quantity: 1));
        await _runtime.SendAsync(new ReleaseStock("B-1"));

        Assert.Equal(["StockReserved", "StockReleased"], _runtime.StateOf<EventLog>("B-1").Events);
    }

    [Fact]
    public async Task everything_crosses_the_runtime_as_json()
    {
        await _runtime.SendAsync(new PlaceOrder("A-1", Quantity: 1, Amount: 50));

        // History is read back from the store: these are deserialized copies, equal by value.
        Assert.Equal(new OrderPlaced("A-1", 1, 50), _runtime.History[0]);
    }

    // What the process produced, without the OrderPlaced that started it.
    private List<object> Events() => [.. _runtime.History.Where(e => e is not OrderPlaced)];
}
