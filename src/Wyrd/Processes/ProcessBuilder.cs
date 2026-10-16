using System.Collections.Immutable;

namespace Wyrd.Processes;

/// <summary>
/// Immutable fluent builder. Each call returns a new builder; modifiers apply to the last node.
/// Steps are typed against the trigger: passing a function that doesn't accept it doesn't compile.
/// </summary>
public sealed class ProcessBuilder<TTrigger>
    where TTrigger : IEvent
{
    private readonly string _name;
    private readonly Func<object, object> _keyOf;
    private readonly ImmutableArray<ProcessNode> _nodes;
    private readonly StepRef? _onCompensated;

    internal ProcessBuilder(string name, Func<object, object> keyOf, ImmutableArray<ProcessNode> nodes, StepRef? onCompensated = null)
    {
        _name = name;
        _keyOf = keyOf;
        _nodes = nodes;
        _onCompensated = onCompensated;
    }

    public ProcessBuilder<TTrigger> Then<TCommand>(Func<TTrigger, TCommand> step)
        where TCommand : ICommand =>
        Append(new StepNode(StepRef.Typed(step)));

    /// <summary>Waits for an external event. <paramref name="key"/> must match the process key.</summary>
    public ProcessBuilder<TTrigger> Await<TEvent>(Func<TEvent, object> key)
        where TEvent : IEvent
    {
        ArgumentNullException.ThrowIfNull(key);
        return Append(new AwaitNode(typeof(TEvent)) { KeyOf = e => key((TEvent)e) });
    }

    /// <summary>
    /// The command sent once compensation has finished, so the business learns how the process ended
    /// (e.g. <c>.OnCompensated(Orders.Cancel)</c>).
    /// </summary>
    public ProcessBuilder<TTrigger> OnCompensated<TCommand>(Func<TTrigger, TCommand> outcome)
        where TCommand : ICommand =>
        Append(new CompensatedNode(StepRef.Typed(outcome)));

    public ProcessBuilder<TTrigger> Undo<TCommand>(Func<TTrigger, TCommand> compensation)
        where TCommand : ICommand =>
        ModifyLast(n => NodeModifiers.Undo(n, StepRef.Typed(compensation)));

    public ProcessBuilder<TTrigger> Retry(Attempts attempts, Backoff backoff = Backoff.Exponential) =>
        ModifyLast(n => NodeModifiers.Retry(n, attempts, backoff));

    public ProcessBuilder<TTrigger> Within(TimeSpan timeout) => ModifyLast(n => NodeModifiers.Within(n, timeout));

    public Process ToProcess() => new(_name, typeof(TTrigger), _keyOf, _nodes, _onCompensated);

    public static implicit operator Process(ProcessBuilder<TTrigger> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.ToProcess();
    }

    internal ProcessBuilder<TTrigger> Append(ProcessNode node)
    {
        NodeModifiers.EnsureAccepts(node, typeof(TTrigger));
        if (node is CompensatedNode outcome)
        {
            return _onCompensated is null
                ? new(_name, _keyOf, _nodes, outcome.Action)
                : throw new InvalidOperationException("A process has a single OnCompensated outcome.");
        }

        return new(_name, _keyOf, _nodes.Add(node), _onCompensated);
    }

    private ProcessBuilder<TTrigger> ModifyLast(Func<ProcessNode, ProcessNode> modify)
    {
        if (_nodes.IsEmpty)
        {
            throw new InvalidOperationException("Modifiers need a preceding Then() or Await().");
        }

        var modified = modify(_nodes[^1]);
        NodeModifiers.EnsureAccepts(modified, typeof(TTrigger));
        return new(_name, _keyOf, _nodes.SetItem(_nodes.Length - 1, modified), _onCompensated);
    }
}
