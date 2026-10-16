using Wyrd.Pipe;
using Wyrd.Processes;

namespace Wyrd.Tests;

public class ProcessDslTests
{
    [Fact]
    public void fluent_and_pipe_build_the_same_process()
    {
        Process fluent = FluentProcess.OrderFulfillment;
        Process pipe = PipeProcess.OrderFulfillment;

        Assert.Equal(fluent, pipe);
        Assert.Equal(fluent.GetHashCode(), pipe.GetHashCode());
        Assert.Equal(fluent.ToMermaid(), pipe.ToMermaid());
    }

    [Fact]
    public void the_process_is_captured_as_data()
    {
        var process = FluentProcess.OrderFulfillment;

        Assert.Equal("OrderFulfillment", process.Name);
        Assert.Equal(typeof(OrderPlaced), process.Trigger);
        Assert.Collection(
            process.Nodes,
            n => Assert.Equal(new StepNode(new("Stock.Reserve"), new("Stock.Release")), n),
            n => Assert.Equal(new StepNode(new("Payment.Charge"), new("Payment.Refund"), new(3.Times, Backoff.Exponential)), n),
            n => Assert.Equal(new AwaitNode(typeof(PaymentConfirmed), TimeSpan.FromMinutes(15)), n),
            n => Assert.Equal(new StepNode(new("Shipping.Ship")), n));
    }

    [Fact]
    public void a_different_process_is_not_equal()
    {
        Process withoutRetry = Process.StartOn<OrderPlaced>(e => e.OrderId).Then(Payment.Charge);
        Process withRetry = Process.StartOn<OrderPlaced>(e => e.OrderId).Then(Payment.Charge).Retry(3.Times);

        Assert.NotEqual(withoutRetry, withRetry);
    }

    [Fact]
    public void pipe_rejects_a_step_that_does_not_accept_the_trigger()
    {
        static ShipOrder FromConfirmation(PaymentConfirmed e) => new(e.OrderId);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            Start.On<OrderPlaced>(e => e.OrderId) >> Pipeline.Step(FromConfirmation));

        Assert.Contains("takes PaymentConfirmed, but the process starts on OrderPlaced", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void undo_cannot_follow_an_await()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            Process.StartOn<OrderPlaced>(e => e.OrderId).Await<PaymentConfirmed>(e => e.OrderId).Undo(Stock.Release));

        Assert.Contains("Undo() can only follow a step", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void within_cannot_follow_a_step() =>
        Assert.Throws<InvalidOperationException>(() =>
            Process.StartOn<OrderPlaced>(e => e.OrderId).Then(Stock.Reserve).Within(5.Minutes));

    [Fact]
    public void modifiers_need_a_preceding_node() =>
        Assert.Throws<InvalidOperationException>(() => Process.StartOn<OrderPlaced>(e => e.OrderId).Undo(Stock.Release));

    [Fact]
    public void renders_living_documentation_as_mermaid()
    {
        const string expected = """
            flowchart TD
                start(["OrderPlaced"])
                n1["Stock.Reserve"]
                n1 -. undo .-> n1u["Stock.Release"]
                start --> n1
                n2["Payment.Charge<br/>retry 3x exponential"]
                n2 -. undo .-> n2u["Payment.Refund"]
                n1 --> n2
                n3{{"await PaymentConfirmed<br/>within 15m"}}
                n2 --> n3
                n4["Shipping.Ship"]
                n3 --> n4
                n4 --> done(["completed"])

            """;

        Assert.Equal(expected.ReplaceLineEndings("\n"), FluentProcess.OrderFulfillment.ToMermaid().ReplaceLineEndings("\n"));
    }
}
