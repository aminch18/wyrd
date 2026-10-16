using Wyrd.Processes;

namespace Wyrd.Runtime;

internal sealed class ProcessInstance(Process process, object key, object trigger)
{
    public Process Process { get; } = process;

    public object Key { get; } = key;

    public object Trigger { get; } = trigger;

    public int Position { get; set; }

    public ProcessStatus Status { get; set; } = ProcessStatus.Running;

    public TimeSpan? Deadline { get; set; }

    /// <summary>Completed steps, most recent first: compensations run in this order.</summary>
    public Stack<StepNode> Completed { get; } = new();

    public AwaitNode? Awaiting => Status == ProcessStatus.Awaiting ? (AwaitNode)Process.Nodes[Position] : null;
}
