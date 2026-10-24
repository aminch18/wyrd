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
    private readonly CompensatedNode? _outcome;
    private readonly bool _outcomeIsLast;

    internal ProcessBuilder(
        string name, Func<object, object> keyOf, ImmutableArray<ProcessNode> nodes, CompensatedNode? outcome = null, bool outcomeIsLast = false)
    {
        _name = name;
        _keyOf = keyOf;
        _nodes = nodes;
        _outcome = outcome;
        _outcomeIsLast = outcomeIsLast;
    }

    /// <summary>Appends a command-producing step that reads the trigger. Declare its retry and compensation policies next.</summary>
    public ProcessBuilder<TTrigger> Then<TCommand>(Func<TTrigger, TCommand> step)
        where TCommand : ICommand =>
        Append(new StepNode(StepRef.Typed(step)));

    /// <summary>
    /// A step that also reads events earlier steps produced (or the process awaited), after the trigger:
    /// <c>.Then(Shipping.Ship)</c> with <c>ShipOrder Ship(OrderPlaced order, PaymentCharged charged)</c>.
    /// </summary>
    public ProcessBuilder<TTrigger> Then(Delegate step) => Append(new StepNode(StepRef.Untyped(step)));

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

    /// <summary>
    /// How the last step is undone if a later step fails. Undoing always insists — 10 retries by default —
    /// because a compensation that gives up leaves the world inconsistent; adjust it with <paramref name="insist"/>.
    /// </summary>
    public ProcessBuilder<TTrigger> Undo<TCommand>(Func<TTrigger, TCommand> compensation, Attempts? insist = null, Backoff backoff = Backoff.Exponential)
        where TCommand : ICommand =>
        ModifyLast(n => NodeModifiers.Undo(n, StepRef.Typed(compensation), insist is { } a ? new RetryPolicy(a, backoff) : null));

    /// <summary>An undo that also reads events produced before its step, after the trigger.</summary>
    public ProcessBuilder<TTrigger> Undo(Delegate compensation, Attempts? insist = null, Backoff backoff = Backoff.Exponential) =>
        ModifyLast(n => NodeModifiers.Undo(n, StepRef.Untyped(compensation), insist is { } a ? new RetryPolicy(a, backoff) : null));

    /// <summary>
    /// When the last step's retries run out, compensate automatically instead of suspending the instance for a human.
    /// Reads as "retry three times, or compensate": <c>.Retry(3.Times).OrCompensate()</c>.
    /// </summary>
    public ProcessBuilder<TTrigger> OrCompensate() => ModifyLast(NodeModifiers.OrCompensate);

    /// <summary>Declares that the last step is not retried, and why (e.g. "the carrier retries on its side").</summary>
    public ProcessBuilder<TTrigger> NoRetry(string reason) => ModifyLast(n => NodeModifiers.NoRetry(n, reason));

    /// <summary>
    /// Declares that the last step needs no undo if a later step fails, and why (e.g. "only sends an email").
    /// Leaving it unsaid is reported by WYRD0011.
    /// </summary>
    public ProcessBuilder<TTrigger> NoCompensation(string reason) => ModifyLast(n => NodeModifiers.NoCompensation(n, reason));

    /// <summary>Sets retries for the preceding step or compensation outcome. Exhausted forward steps suspend unless <c>OrCompensate()</c> is selected.</summary>
    public ProcessBuilder<TTrigger> Retry(Attempts attempts, Backoff backoff = Backoff.Exponential) =>
        ModifyLast(n => NodeModifiers.Retry(n, attempts, backoff));

    /// <summary>Sets the business deadline of the preceding wait; expiry starts compensation.</summary>
    public ProcessBuilder<TTrigger> Within(TimeSpan timeout) => ModifyLast(n => NodeModifiers.Within(n, timeout));

    /// <summary>Builds the immutable process definition for registration, rendering or comparison.</summary>
    public Process ToProcess() => new(_name, typeof(TTrigger), _keyOf, _nodes, _outcome);

    /// <summary>Builds the process definition when assigning a fluent builder to a <see cref="Process"/>.</summary>
    public static implicit operator Process(ProcessBuilder<TTrigger> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.ToProcess();
    }

    internal ProcessBuilder<TTrigger> Append(ProcessNode node)
    {
        NodeModifiers.EnsureAccepts(node, typeof(TTrigger));
        node = NodeModifiers.Bind<TTrigger>(node);
        if (node is CompensatedNode outcome)
        {
            return _outcome is null
                ? new(_name, _keyOf, _nodes, outcome, outcomeIsLast: true)
                : throw new InvalidOperationException("A process has a single OnCompensated outcome.");
        }

        return new(_name, _keyOf, _nodes.Add(node), _outcome);
    }

    private ProcessBuilder<TTrigger> ModifyLast(Func<ProcessNode, ProcessNode> modify)
    {
        if (_outcomeIsLast)
        {
            return new(_name, _keyOf, _nodes, (CompensatedNode)modify(_outcome!), outcomeIsLast: true);
        }

        if (_nodes.IsEmpty)
        {
            throw new InvalidOperationException("Modifiers need a preceding Then() or Await().");
        }

        var modified = modify(_nodes[^1]);
        NodeModifiers.EnsureAccepts(modified, typeof(TTrigger));
        modified = NodeModifiers.Bind<TTrigger>(modified);
        return new(_name, _keyOf, _nodes.SetItem(_nodes.Length - 1, modified), _outcome);
    }
}
