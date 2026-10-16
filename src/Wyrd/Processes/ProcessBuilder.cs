using System.Collections.Immutable;

namespace Wyrd.Processes;

/// <summary>Immutable fluent builder. Each call returns a new builder; modifiers apply to the last node.</summary>
public sealed class ProcessBuilder
{
    private readonly Type _trigger;
    private readonly ImmutableArray<ProcessNode> _nodes;

    internal ProcessBuilder(Type trigger, ImmutableArray<ProcessNode> nodes)
    {
        _trigger = trigger;
        _nodes = nodes;
    }

    public ProcessBuilder Then(Delegate step) => Append(new StepNode(StepRef.From(step)));

    public ProcessBuilder Await<TEvent>() => Append(new AwaitNode(typeof(TEvent)));

    public ProcessBuilder Undo(Delegate compensation) => ModifyLast(n => NodeModifiers.Undo(n, compensation));

    public ProcessBuilder Retry(Attempts attempts, Backoff backoff = Backoff.Exponential) =>
        ModifyLast(n => NodeModifiers.Retry(n, attempts, backoff));

    public ProcessBuilder Within(TimeSpan timeout) => ModifyLast(n => NodeModifiers.Within(n, timeout));

    public Process ToProcess() => new(_trigger, _nodes);

    public static implicit operator Process(ProcessBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.ToProcess();
    }

    internal ProcessBuilder Append(ProcessNode node) => new(_trigger, _nodes.Add(node));

    private ProcessBuilder ModifyLast(Func<ProcessNode, ProcessNode> modify) =>
        _nodes.IsEmpty
            ? throw new InvalidOperationException("Modifiers need a preceding Then() or Await().")
            : new(_trigger, _nodes.SetItem(_nodes.Length - 1, modify(_nodes[^1])));
}
