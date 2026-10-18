using Wyrd.Processes;
using Wyrd.Runtime;

namespace Wyrd.Tests;

public class OrderedProjectionTests
{
    // Every slice of the test domain stores an EventLog keyed by order id: all events of an order are one stream.
    private static readonly Process Straight = Process.StartOn<OrderPlaced>(e => e.OrderId, "Straight")
        .Then(Stock.Reserve).NoRetry("test").Undo(Stock.Release)
        .Then(Payment.Charge).NoRetry("test").Undo(Payment.Refund)
        .Then(Shipping.Ship).NoRetry("test");

    /// <summary>Holds events back while anything else can run, then delivers the newest first: the worst order.</summary>
    private sealed class NewestEventsFirst : ISimulation
    {
        public int PickNext(IReadOnlyList<Envelope> queue)
        {
            for (var i = 0; i < queue.Count; i++)
            {
                if (queue[i].Kind != MessageKind.Event)
                {
                    return i;
                }
            }

            return queue.Count - 1;
        }

        public bool Duplicate(Envelope envelope) => false;

        public bool Fault(Envelope command, Type commandType) => false;
    }

    private static InMemoryRuntime Reordering()
    {
        var runtime = TestSystem.Create(Straight).AddProjection(new OrderSummaries());
        runtime.Simulation = new NewestEventsFirst();
        return runtime;
    }

    [Fact]
    public async Task a_view_sees_the_events_of_a_stream_in_the_order_they_happened()
    {
        var runtime = Reordering();

        await runtime.SendAsync(new PlaceOrder("A-1", Quantity: 1, Amount: 50));

        // Delivered as OrderShipped, PaymentCharged, StockReserved; projected as they happened. Without ordering the
        // late PaymentCharged would move the order back to "Paid".
        Assert.Equal(ProcessStatus.Completed, runtime.StatusOf(Straight, "A-1"));
        Assert.Equal(new OrderSummary("A-1", 50, "Shipped", Charges: 1), runtime.View<OrderSummary>("A-1"));
        Assert.Contains(runtime.Journal, e => e.Message == "OrderShipped held for read models until earlier events of its stream arrive");
    }

    [Fact]
    public async Task processes_do_not_wait_for_read_models()
    {
        var runtime = Reordering();

        await runtime.SendAsync(new PlaceOrder("A-1", Quantity: 1, Amount: 50));

        var shipped = runtime.Journal.ToList().FindIndex(e => e.Message == "-> ShipOrder");
        var held = runtime.Journal.ToList().FindIndex(e => e.Message.EndsWith("held for read models until earlier events of its stream arrive", StringComparison.Ordinal));
        Assert.True(shipped < held, "the process shipped before any event reached the read model");
    }

    [Fact]
    public async Task events_from_outside_have_no_stream_and_are_projected_as_they_come()
    {
        var runtime = TestSystem.Create(FluentProcess.OrderFulfillment).AddProjection(new OrderSummaries());

        await runtime.SendAsync(new PlaceOrder("A-1", Quantity: 1, Amount: 50));
        await runtime.PublishAsync(new PaymentConfirmed("A-1"));

        Assert.Equal(new OrderSummary("A-1", 50, "Shipped", Charges: 1), runtime.View<OrderSummary>("A-1"));
    }
}
