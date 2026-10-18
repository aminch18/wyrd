using Wyrd.Pipe;
using Wyrd.Processes;
using Wyrd.Runtime;
using static Wyrd.Pipe.Pipeline;

namespace Wyrd.Tests;

// Steps that read what earlier steps produced, besides the trigger.
public static class Paid
{
    public static ShipOrder Ship(OrderPlaced order, PaymentCharged charged) => new($"{order.OrderId}/{charged.Amount}");

    public static ShipOrder ShipConfirmed(OrderPlaced order, PaymentCharged charged, PaymentConfirmed confirmed) =>
        new($"{confirmed.OrderId}/{charged.Amount}");

    public static RefundPayment RefundAfterShipping(OrderPlaced order, StockReserved reserved) => new(reserved.OrderId);
}

public class ReadingEventsTests
{
    private static Process Fluent => Process.StartOn<OrderPlaced>(e => e.OrderId, "Paid")
        .Then(Stock.Reserve).NoRetry("test").Undo(Stock.Release)
        .Then(Payment.Charge).NoRetry("test").Undo(Payment.Refund)
        .Then(Paid.Ship).NoRetry("test");

    private static Process Piped =>
        Start.On<OrderPlaced>(e => e.OrderId, "Paid")
        >> Step(Stock.Reserve).NoRetry("test").Undo(Stock.Release)
        >> Step(Payment.Charge).NoRetry("test").Undo(Payment.Refund)
        >> Step(Paid.Ship).NoRetry("test");

    [Fact]
    public async Task a_step_reads_the_event_an_earlier_step_produced()
    {
        var runtime = TestSystem.Create(Fluent);

        await runtime.SendAsync(new PlaceOrder("A-1", Quantity: 1, Amount: 50));

        Assert.Equal(ProcessStatus.Completed, runtime.StatusOf(Fluent, "A-1"));
        Assert.Contains(new OrderShipped("A-1/50"), runtime.History);
    }

    [Fact]
    public void fluent_and_pipe_build_the_same_process_with_reads()
    {
        Assert.Equal(Fluent, Piped);
        Assert.Equal([typeof(PaymentCharged)], ((StepNode)Piped.Nodes[2]).Action.Reads);
    }

    [Fact]
    public async Task a_step_reads_awaited_events_too()
    {
        Process process = Process.StartOn<OrderPlaced>(e => e.OrderId, "Confirmed")
            .Then(Payment.Charge).NoRetry("test").Undo(Payment.Refund)
            .Await<PaymentConfirmed>(e => e.OrderId).Within(15.Minutes)
            .Then(Paid.ShipConfirmed).NoRetry("test");
        var runtime = TestSystem.Create(process);

        await runtime.SendAsync(new PlaceOrder("A-1", Quantity: 1, Amount: 50));
        await runtime.PublishAsync(new PaymentConfirmed("A-1"));

        Assert.Contains(new OrderShipped("A-1/50"), runtime.History);
    }

    [Fact]
    public async Task an_undo_reads_events_from_before_its_step()
    {
        Process process = Process.StartOn<OrderPlaced>(e => e.OrderId, "UndoReads")
            .Then(Stock.Reserve).NoRetry("test").Undo(Stock.Release)
            .Then(Payment.Charge).NoRetry("test").Undo(Paid.RefundAfterShipping)
            .Then(Shipping.Ship).NoRetry("test");
        var runtime = TestSystem.Create(process);
        runtime.FailNext<ShipOrder>();

        await runtime.SendAsync(new PlaceOrder("A-1", Quantity: 1, Amount: 50));

        Assert.Equal(ProcessStatus.Suspended, runtime.StatusOf(process, "A-1"));
        await runtime.CompensateAsync(process, "A-1");
        Assert.Contains(new PaymentRefunded("A-1"), runtime.History);
    }

    [Fact]
    public async Task reading_an_event_no_earlier_step_produces_is_rejected_before_anything_runs()
    {
        Process process = Process.StartOn<OrderPlaced>(e => e.OrderId, "TooEarly")
            .Then(Paid.Ship).NoRetry("test")
            .Then(Payment.Charge).NoRetry("test");
        var runtime = TestSystem.Create(process);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.SendAsync(new PlaceOrder("A-1", 1, 50)));

        Assert.Contains("step 'Paid.Ship' reads PaymentCharged, but no earlier step produces it", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task an_undo_can_not_read_its_own_steps_event()
    {
        Process process = Process.StartOn<OrderPlaced>(e => e.OrderId, "OwnEvent")
            .Then(Payment.Charge).NoRetry("test").Undo((OrderPlaced order, PaymentCharged charged) => new RefundPayment(charged.OrderId))
            .Then(Shipping.Ship).NoRetry("test");
        var runtime = TestSystem.Create(process);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.SendAsync(new PlaceOrder("A-1", 1, 50)));

        Assert.Contains("an undo those before its own step", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void a_step_whose_first_parameter_is_not_the_trigger_is_rejected()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            Start.On<PaymentConfirmed>(e => e.OrderId, "WrongTrigger") >> Step(Paid.Ship).NoRetry("test"));

        Assert.Contains("takes OrderPlaced, but the process starts on PaymentConfirmed", ex.Message, StringComparison.Ordinal);
    }
}
