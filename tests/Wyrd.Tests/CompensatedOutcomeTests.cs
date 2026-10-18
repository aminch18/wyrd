using Wyrd.Pipe;
using Wyrd.Processes;
using Wyrd.Runtime;

namespace Wyrd.Tests;

public class CompensatedOutcomeTests
{
    private static Process Fluent => Process
        .StartOn<OrderPlaced>(e => e.OrderId)
        .Then(Stock.Reserve).Undo(Stock.Release)
        .Then(Payment.Charge).Undo(Payment.Refund)
        .OnCompensated(Orders.Cancel);

    private static Process Piped =>
        Start.On<OrderPlaced>(e => e.OrderId, nameof(Fluent))
          >> Pipeline.Step(Stock.Reserve).Undo(Stock.Release)
          >> Pipeline.Step(Payment.Charge).Undo(Payment.Refund)
          >> Pipeline.OnCompensated(Orders.Cancel);

    [Fact]
    public void both_syntaxes_capture_the_outcome()
    {
        Assert.Equal(Fluent, Piped);
        Assert.Equal(new StepRef("Orders.Cancel"), Fluent.OnCompensated);
        Assert.Contains("compensated([\"compensated\"]) --> outcome[\"Orders.Cancel\"]", Fluent.ToMermaid(), StringComparison.Ordinal);
    }

    [Fact]
    public void a_process_has_a_single_outcome() =>
        Assert.Throws<InvalidOperationException>(() =>
            Process.StartOn<OrderPlaced>(e => e.OrderId).OnCompensated(Orders.Cancel).OnCompensated(Orders.Cancel));

    [Fact]
    public async Task the_outcome_runs_after_every_undo()
    {
        var runtime = TestSystem.Create(Fluent);

        await runtime.SendAsync(new PlaceOrder("A-1", Quantity: 1, Amount: 5_000)); // declined

        Assert.Equal(ProcessStatus.Compensated, runtime.StatusOf(Fluent, "A-1"));
        Assert.Equal(
            [new OrderPlaced("A-1", 1, 5_000), new StockReserved("A-1"), new StockReleased("A-1"), new OrderCancelled("A-1")],
            runtime.History);
    }

    [Fact]
    public async Task the_outcome_runs_even_when_there_was_nothing_to_undo()
    {
        var runtime = TestSystem.Create(Fluent);

        await runtime.SendAsync(new PlaceOrder("A-1", Quantity: 99, Amount: 50)); // out of stock

        Assert.Equal(ProcessStatus.Compensated, runtime.StatusOf(Fluent, "A-1"));
        Assert.Contains(new OrderCancelled("A-1"), runtime.History);
    }

    [Fact]
    public async Task a_failed_outcome_needs_attention()
    {
        var runtime = TestSystem.Create(Fluent);
        runtime.FailNext<CancelOrder>();

        await runtime.SendAsync(new PlaceOrder("A-1", Quantity: 99, Amount: 50));

        Assert.Equal(ProcessStatus.Failed, runtime.StatusOf(Fluent, "A-1"));
        Assert.Contains(runtime.Journal, e => e.Message == "FAILED: Orders.Cancel could not run, needs attention");
    }

    [Fact]
    public async Task a_completed_process_never_runs_the_outcome()
    {
        var runtime = TestSystem.Create(Fluent);

        await runtime.SendAsync(new PlaceOrder("A-1", Quantity: 1, Amount: 50));

        Assert.Equal(ProcessStatus.Completed, runtime.StatusOf(Fluent, "A-1"));
        Assert.DoesNotContain(new OrderCancelled("A-1"), runtime.History);
    }

    [Fact]
    public async Task the_outcome_can_retry()
    {
        Process process = Process
            .StartOn<OrderPlaced>(e => e.OrderId)
            .Then(Stock.Reserve).Undo(Stock.Release)
            .OnCompensated(Orders.Cancel).Retry(3.Times);
        var runtime = TestSystem.Create(process);
        runtime.FailNext<CancelOrder>(times: 2);

        await runtime.SendAsync(new PlaceOrder("A-1", Quantity: 99, Amount: 50));
        await runtime.AdvanceAsync(10.Seconds);

        Assert.Equal(ProcessStatus.Compensated, runtime.StatusOf(process, "A-1"));
        Assert.Contains(new OrderCancelled("A-1"), runtime.History);
    }

    [Fact]
    public void the_outcome_retry_is_part_of_the_model()
    {
        Process fluent = Process.StartOn<OrderPlaced>(e => e.OrderId, "P").Then(Stock.Reserve).OnCompensated(Orders.Cancel).Retry(3.Times);
        Process piped = Start.On<OrderPlaced>(e => e.OrderId, "P") >> Pipeline.Step(Stock.Reserve) >> Pipeline.OnCompensated(Orders.Cancel).Retry(3.Times);

        Assert.Equal(fluent, piped);
        Assert.Equal(new RetryPolicy(3.Times, Backoff.Exponential), fluent.Outcome!.Retry);
        Assert.Contains("Orders.Cancel<br/>retry 3x exponential", fluent.ToMermaid(), StringComparison.Ordinal);
    }

    [Fact]
    public void undo_still_cannot_follow_the_outcome() =>
        Assert.Throws<InvalidOperationException>(() =>
            Process.StartOn<OrderPlaced>(e => e.OrderId).Then(Stock.Reserve).OnCompensated(Orders.Cancel).Undo(Stock.Release));
}
