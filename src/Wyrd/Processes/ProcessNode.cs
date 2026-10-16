namespace Wyrd.Processes;

public enum Backoff
{
    Immediate,
    Linear,
    Exponential,
}

public sealed record RetryPolicy(Attempts Attempts, Backoff Backoff);

/// <summary>A stable, human-readable reference to a step function, e.g. <c>Payment.Charge</c>.</summary>
public sealed record StepRef(string Name)
{
    public static StepRef From(Delegate step)
    {
        ArgumentNullException.ThrowIfNull(step);
        return new($"{step.Method.DeclaringType?.Name}.{step.Method.Name}");
    }

    public override string ToString() => Name;
}

public abstract record ProcessNode;

public sealed record StepNode(StepRef Action, StepRef? Compensation = null, RetryPolicy? Retry = null) : ProcessNode;

public sealed record AwaitNode(Type Event, TimeSpan? Timeout = null) : ProcessNode;

/// <summary>Modifiers shared by the fluent and the pipe syntax, so both build the exact same model.</summary>
internal static class NodeModifiers
{
    public static ProcessNode Undo(ProcessNode node, Delegate compensation) => node switch
    {
        StepNode step => step with { Compensation = StepRef.From(compensation) },
        _ => throw Misplaced(nameof(Undo), "a step", node),
    };

    public static ProcessNode Retry(ProcessNode node, Attempts attempts, Backoff backoff) => node switch
    {
        StepNode step => step with { Retry = new RetryPolicy(attempts, backoff) },
        _ => throw Misplaced(nameof(Retry), "a step", node),
    };

    public static ProcessNode Within(ProcessNode node, TimeSpan timeout) => node switch
    {
        AwaitNode wait => wait with { Timeout = timeout },
        _ => throw Misplaced(nameof(Within), "an Await", node),
    };

    private static InvalidOperationException Misplaced(string modifier, string target, ProcessNode node) =>
        new($"{modifier}() can only follow {target}, but it follows {node}.");
}
