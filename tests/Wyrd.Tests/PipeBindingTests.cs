using Wyrd.Pipe;
using Wyrd.Processes;
using Wyrd.Runtime;

namespace Wyrd.Tests;

public class PipeBindingTests
{
    [Fact]
    public async Task a_process_written_with_pipes_runs_end_to_end()
    {
        var runtime = TestSystem.Create(PipeProcess.OrderFulfillment);

        await runtime.SendAsync(new PlaceOrder("A-1", Quantity: 1, Amount: 50));
        await runtime.PublishAsync(new PaymentConfirmed("A-1"));

        Assert.Equal(ProcessStatus.Completed, runtime.StatusOf(PipeProcess.OrderFulfillment, "A-1"));
        Assert.Contains(new OrderShipped("A-1"), runtime.History);
    }

    [Fact]
    public async Task a_pipe_step_may_take_a_more_general_type_than_the_trigger()
    {
        static ShipOrder ShipAnything(IDomainEvent e) => new(((OrderPlaced)e).OrderId);
        Process process = Start.On<OrderPlaced>(e => e.OrderId, "General") >> Pipeline.Step(ShipAnything);
        var runtime = TestSystem.Create(process);

        await runtime.SendAsync(new PlaceOrder("A-1", Quantity: 1, Amount: 50));

        Assert.Equal(ProcessStatus.Completed, runtime.StatusOf(process, "A-1"));
    }
}
