using Wyrd.Processes;
using Wyrd.Runtime;

namespace Wyrd.Tests;

public class RuntimeTests
{
    private static readonly Process Fulfillment = FluentProcess.OrderFulfillment;

    private readonly InMemoryRuntime _runtime = TestSystem.Create(Fulfillment);

    [Fact]
    public void happy_path_waits_for_confirmation_then_completes()
    {
        _runtime.Send(new PlaceOrder("A-1", Quantity: 1, Amount: 50));
        Assert.Equal(ProcessStatus.Awaiting, _runtime.StatusOf(Fulfillment, "A-1"));

        _runtime.Publish(new PaymentConfirmed("A-1"));

        Assert.Equal(ProcessStatus.Completed, _runtime.StatusOf(Fulfillment, "A-1"));
        Assert.Equal(
            [new StockReserved("A-1"), new PaymentCharged("A-1", 50), new OrderShipped("A-1")],
            Events());
    }

    [Fact]
    public void a_rejected_step_compensates_only_what_completed()
    {
        _runtime.Send(new PlaceOrder("A-1", Quantity: 1, Amount: 5_000));

        Assert.Equal(ProcessStatus.Compensated, _runtime.StatusOf(Fulfillment, "A-1"));
        Assert.Equal([new StockReserved("A-1"), new StockReleased("A-1")], Events());
    }

    [Fact]
    public void a_rejected_first_step_has_nothing_to_compensate()
    {
        _runtime.Send(new PlaceOrder("A-1", Quantity: 99, Amount: 50));

        Assert.Equal(ProcessStatus.Compensated, _runtime.StatusOf(Fulfillment, "A-1"));
        Assert.Empty(Events());
    }

    [Fact]
    public void transient_failures_are_retried()
    {
        _runtime.FailNext<ChargePayment>(times: 2);

        _runtime.Send(new PlaceOrder("A-1", Quantity: 1, Amount: 50));

        Assert.Equal(ProcessStatus.Awaiting, _runtime.StatusOf(Fulfillment, "A-1"));
        Assert.Equal(2, _runtime.Journal.Count(e => e.Message.StartsWith("transient failure, retry", StringComparison.Ordinal)));
    }

    [Fact]
    public void exhausted_retries_compensate()
    {
        _runtime.FailNext<ChargePayment>(times: 4);

        _runtime.Send(new PlaceOrder("A-1", Quantity: 1, Amount: 50));

        Assert.Equal(ProcessStatus.Compensated, _runtime.StatusOf(Fulfillment, "A-1"));
        Assert.Equal([new StockReserved("A-1"), new StockReleased("A-1")], Events());
    }

    [Fact]
    public void a_timeout_compensates_in_reverse_order()
    {
        _runtime.Send(new PlaceOrder("A-1", Quantity: 1, Amount: 50));

        _runtime.Advance(14.Minutes);
        Assert.Equal(ProcessStatus.Awaiting, _runtime.StatusOf(Fulfillment, "A-1"));

        _runtime.Advance(1.Minutes);
        Assert.Equal(ProcessStatus.Compensated, _runtime.StatusOf(Fulfillment, "A-1"));
        Assert.Equal(
            [new StockReserved("A-1"), new PaymentCharged("A-1", 50), new PaymentRefunded("A-1"), new StockReleased("A-1")],
            Events());
    }

    [Fact]
    public void a_confirmation_after_the_timeout_is_ignored()
    {
        _runtime.Send(new PlaceOrder("A-1", Quantity: 1, Amount: 50));
        _runtime.Advance(15.Minutes);

        _runtime.Publish(new PaymentConfirmed("A-1"));

        Assert.Equal(ProcessStatus.Compensated, _runtime.StatusOf(Fulfillment, "A-1"));
        Assert.DoesNotContain(new OrderShipped("A-1"), Events());
    }

    [Fact]
    public void placing_the_same_order_twice_is_rejected_by_the_decision()
    {
        _runtime.Send(new PlaceOrder("A-1", Quantity: 1, Amount: 50));
        _runtime.Send(new PlaceOrder("A-1", Quantity: 1, Amount: 50));

        Assert.Single(Events().OfType<PaymentCharged>());
        Assert.Contains(_runtime.Journal, e => e.Message == "rejected AlreadyExists");
    }

    [Fact]
    public void a_redelivered_integration_event_starts_a_single_instance()
    {
        Process shipWhenPaid = Process.StartOn<PaymentChargedV1>(e => e.OrderId).Then(Shipping.ShipPaid);
        var runtime = TestSystem.Create(shipWhenPaid);

        runtime.Publish(new PaymentChargedV1("A-1", 50));
        runtime.Publish(new PaymentChargedV1("A-1", 50));

        Assert.Single(runtime.History.OfType<OrderShipped>());
        Assert.Contains(runtime.Journal, e => e.Message == "duplicate PaymentChargedV1 ignored");
    }

    [Fact]
    public void confirmations_are_correlated_by_key()
    {
        _runtime.Send(new PlaceOrder("A-1", Quantity: 1, Amount: 50));
        _runtime.Send(new PlaceOrder("A-2", Quantity: 1, Amount: 50));

        _runtime.Publish(new PaymentConfirmed("A-2"));

        Assert.Equal(ProcessStatus.Awaiting, _runtime.StatusOf(Fulfillment, "A-1"));
        Assert.Equal(ProcessStatus.Completed, _runtime.StatusOf(Fulfillment, "A-2"));
    }

    [Fact]
    public void a_failed_compensation_needs_attention()
    {
        _runtime.FailNext<ReleaseStock>();

        _runtime.Send(new PlaceOrder("A-1", Quantity: 1, Amount: 5_000));

        Assert.Equal(ProcessStatus.Failed, _runtime.StatusOf(Fulfillment, "A-1"));
    }

    [Fact]
    public void the_same_inputs_produce_the_same_journal()
    {
        static IReadOnlyList<JournalEntry> Run()
        {
            var runtime = TestSystem.Create(Fulfillment);
            runtime.FailNext<ChargePayment>();
            runtime.Send(new PlaceOrder("A-1", Quantity: 1, Amount: 50));
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

    // What the process produced, without the OrderPlaced that started it.
    private List<object> Events() => [.. _runtime.History.Where(e => e is not OrderPlaced)];
}
