using Wyrd.Processes;
using Wyrd.Runtime;

namespace Wyrd.Tests;

public class RuntimeTests
{
    private static readonly Process Fulfillment = FluentProcess.OrderFulfillment;

    private readonly InMemoryRuntime _runtime = new(Fulfillment);

    [Fact]
    public void happy_path_waits_for_confirmation_then_completes()
    {
        _runtime.Publish(new OrderPlaced("A-1", Quantity: 1, Amount: 50));
        Assert.Equal(ProcessStatus.Awaiting, _runtime.StatusOf(Fulfillment, "A-1"));

        _runtime.Publish(new PaymentConfirmed("A-1"));

        Assert.Equal(ProcessStatus.Completed, _runtime.StatusOf(Fulfillment, "A-1"));
        Assert.Equal(
            [new StockReserved("A-1"), new PaymentCharged("A-1"), new OrderShipped("A-1")],
            Events());
    }

    [Fact]
    public void a_rejected_step_compensates_only_what_completed()
    {
        _runtime.Publish(new OrderPlaced("A-1", Quantity: 1, Amount: 5_000));

        Assert.Equal(ProcessStatus.Compensated, _runtime.StatusOf(Fulfillment, "A-1"));
        Assert.Equal([new StockReserved("A-1"), new StockReleased("A-1")], Events());
    }

    [Fact]
    public void a_rejected_first_step_has_nothing_to_compensate()
    {
        _runtime.Publish(new OrderPlaced("A-1", Quantity: 99, Amount: 50));

        Assert.Equal(ProcessStatus.Compensated, _runtime.StatusOf(Fulfillment, "A-1"));
        Assert.Empty(Events());
    }

    [Fact]
    public void transient_failures_are_retried()
    {
        _runtime.FailNext<ChargePayment>(times: 2);

        _runtime.Publish(new OrderPlaced("A-1", Quantity: 1, Amount: 50));

        Assert.Equal(ProcessStatus.Awaiting, _runtime.StatusOf(Fulfillment, "A-1"));
        Assert.Equal(2, _runtime.Journal.Count(e => e.Message.StartsWith("transient failure, retry", StringComparison.Ordinal)));
    }

    [Fact]
    public void exhausted_retries_compensate()
    {
        _runtime.FailNext<ChargePayment>(times: 4);

        _runtime.Publish(new OrderPlaced("A-1", Quantity: 1, Amount: 50));

        Assert.Equal(ProcessStatus.Compensated, _runtime.StatusOf(Fulfillment, "A-1"));
        Assert.Equal([new StockReserved("A-1"), new StockReleased("A-1")], Events());
    }

    [Fact]
    public void a_timeout_compensates_in_reverse_order()
    {
        _runtime.Publish(new OrderPlaced("A-1", Quantity: 1, Amount: 50));

        _runtime.Advance(14.Minutes);
        Assert.Equal(ProcessStatus.Awaiting, _runtime.StatusOf(Fulfillment, "A-1"));

        _runtime.Advance(1.Minutes);
        Assert.Equal(ProcessStatus.Compensated, _runtime.StatusOf(Fulfillment, "A-1"));
        Assert.Equal(
            [new StockReserved("A-1"), new PaymentCharged("A-1"), new PaymentRefunded("A-1"), new StockReleased("A-1")],
            Events());
    }

    [Fact]
    public void a_confirmation_after_the_timeout_is_ignored()
    {
        _runtime.Publish(new OrderPlaced("A-1", Quantity: 1, Amount: 50));
        _runtime.Advance(15.Minutes);

        _runtime.Publish(new PaymentConfirmed("A-1"));

        Assert.Equal(ProcessStatus.Compensated, _runtime.StatusOf(Fulfillment, "A-1"));
        Assert.DoesNotContain(new OrderShipped("A-1"), Events());
    }

    [Fact]
    public void a_duplicated_trigger_starts_a_single_instance()
    {
        _runtime.Publish(new OrderPlaced("A-1", Quantity: 1, Amount: 50));
        _runtime.Publish(new OrderPlaced("A-1", Quantity: 1, Amount: 50));

        Assert.Single(Events().OfType<PaymentCharged>());
        Assert.Contains(_runtime.Journal, e => e.Message == "duplicate OrderPlaced ignored");
    }

    [Fact]
    public void confirmations_are_correlated_by_key()
    {
        _runtime.Publish(new OrderPlaced("A-1", Quantity: 1, Amount: 50));
        _runtime.Publish(new OrderPlaced("A-2", Quantity: 1, Amount: 50));

        _runtime.Publish(new PaymentConfirmed("A-2"));

        Assert.Equal(ProcessStatus.Awaiting, _runtime.StatusOf(Fulfillment, "A-1"));
        Assert.Equal(ProcessStatus.Completed, _runtime.StatusOf(Fulfillment, "A-2"));
    }

    [Fact]
    public void a_failed_compensation_needs_attention()
    {
        _runtime.FailNext<ReleaseStock>();

        _runtime.Publish(new OrderPlaced("A-1", Quantity: 1, Amount: 5_000));

        Assert.Equal(ProcessStatus.Failed, _runtime.StatusOf(Fulfillment, "A-1"));
    }

    [Fact]
    public void the_same_inputs_produce_the_same_journal()
    {
        static IReadOnlyList<JournalEntry> Run()
        {
            var runtime = new InMemoryRuntime(Fulfillment);
            runtime.FailNext<ChargePayment>();
            runtime.Publish(new OrderPlaced("A-1", Quantity: 1, Amount: 50));
            runtime.Advance(20.Minutes);
            return runtime.Journal;
        }

        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void commands_can_be_sent_from_outside_a_process()
    {
        var decision = _runtime.Send(new ChargePayment("B-1", Amount: 5_000));

        Assert.Equal(new Decision.Rejected(PaymentError.Declined), decision);
        Assert.Empty(Events());
    }

    [Fact]
    public void state_is_evolved_from_accepted_events()
    {
        _runtime.Send(new ReserveStock("B-1", Quantity: 1));
        _runtime.Send(new ReleaseStock("B-1"));

        Assert.Equal(
            [new StockReserved("B-1"), new StockReleased("B-1")],
            _runtime.StateOf<EventLog>("B-1").Events);
    }

    private List<object> Events() => [.. _runtime.History];
}
