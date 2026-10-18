namespace Wyrd.Processes;

public enum Backoff
{
    Immediate,
    Linear,
    Exponential,
}

public sealed record RetryPolicy(Attempts Attempts, Backoff Backoff)
{
    /// <summary>Delay before the given retry (1-based).</summary>
    public TimeSpan DelayBefore(int retry) => Backoff switch
    {
        Backoff.Immediate => TimeSpan.Zero,
        Backoff.Linear => TimeSpan.FromSeconds(retry),
        _ => TimeSpan.FromSeconds(Math.Pow(2, retry - 1)),
    };
}

/// <summary>
/// A stable, human-readable reference to a step function, e.g. <c>Payment.Charge</c>.
/// Equality is by name only: two references to the same function are the same step.
/// </summary>
public sealed record StepRef(string Name)
{
    internal Func<object, ICommand>? Invoke { get; init; }

    internal Type? Input { get; init; }

    /// <summary>The command type the step produces.</summary>
    internal Type? Output { get; init; }

    public static StepRef From(Delegate step)
    {
        ArgumentNullException.ThrowIfNull(step);
        return new($"{step.Method.DeclaringType?.Name}.{step.Method.Name}");
    }

    internal static StepRef Typed<TIn, TCommand>(Func<TIn, TCommand> step)
        where TCommand : ICommand =>
        From(step) with { Input = typeof(TIn), Output = typeof(TCommand), Invoke = input => step((TIn)input) };

    internal static StepRef Untyped(Delegate step)
    {
        var reference = From(step);
        var parameters = step.Method.GetParameters();
        if (parameters.Length != 1 || !typeof(ICommand).IsAssignableFrom(step.Method.ReturnType))
        {
            throw new InvalidOperationException(
                $"Step '{reference}' must take exactly one argument and return an ICommand.");
        }

        return reference with
        {
            Input = parameters[0].ParameterType,
            Output = step.Method.ReturnType,
            Invoke = input =>
            {
                try
                {
                    return (ICommand)step.DynamicInvoke(input)!;
                }
                catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException is not null)
                {
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(ex.InnerException);
                    throw;
                }
            },
        };
    }

    public bool Equals(StepRef? other) => other is not null && Name == other.Name;

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Name);

    public override string ToString() => Name;
}

public abstract record ProcessNode;

public sealed record StepNode(StepRef Action, StepRef? Compensation = null, RetryPolicy? Retry = null) : ProcessNode
{
    /// <summary>Why this step needs no undo, when that was decided explicitly with <c>.NoCompensation("why")</c>.</summary>
    public string? NoCompensationReason { get; init; }
}

/// <summary>
/// How the process ends when it is compensated: a command that tells the business (e.g. <c>CancelOrder</c>),
/// so read models and other slices learn the outcome. Not a position in the flow.
/// </summary>
public sealed record CompensatedNode(StepRef Action, RetryPolicy? Retry = null) : ProcessNode;

/// <summary>Waits for an external event correlated to the process instance.</summary>
public sealed record AwaitNode(Type Event, TimeSpan? Timeout = null) : ProcessNode
{
    internal Func<object, object>? KeyOf { get; init; }

    public bool Equals(AwaitNode? other) => other is not null && Event == other.Event && Timeout == other.Timeout;

    public override int GetHashCode() => HashCode.Combine(Event, Timeout);
}

/// <summary>Modifiers shared by the fluent and the pipe syntax, so both build the exact same model.</summary>
internal static class NodeModifiers
{
    public static ProcessNode Undo(ProcessNode node, StepRef compensation) => node switch
    {
        StepNode { NoCompensationReason: not null } => throw new InvalidOperationException(
            "A step is either undone with Undo() or explicitly not with NoCompensation(), never both."),
        StepNode step => step with { Compensation = compensation },
        _ => throw Misplaced(nameof(Undo), "a step", node),
    };

    public static ProcessNode NoCompensation(ProcessNode node, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return node switch
        {
            StepNode { Compensation: not null } => throw new InvalidOperationException(
                "A step is either undone with Undo() or explicitly not with NoCompensation(), never both."),
            StepNode step => step with { NoCompensationReason = reason },
            _ => throw Misplaced(nameof(NoCompensation), "a step", node),
        };
    }

    public static ProcessNode Retry(ProcessNode node, Attempts attempts, Backoff backoff) => node switch
    {
        StepNode step => step with { Retry = new RetryPolicy(attempts, backoff) },
        CompensatedNode outcome => outcome with { Retry = new RetryPolicy(attempts, backoff) },
        _ => throw Misplaced(nameof(Retry), "a step or OnCompensated", node),
    };

    public static ProcessNode Within(ProcessNode node, TimeSpan timeout) => node switch
    {
        AwaitNode wait => wait with { Timeout = timeout },
        _ => throw Misplaced(nameof(Within), "an Await", node),
    };

    /// <summary>Checks that step functions accept the process trigger.</summary>
    public static void EnsureAccepts(ProcessNode node, Type trigger)
    {
        StepRef?[] references = node switch
        {
            StepNode step => [step.Action, step.Compensation],
            CompensatedNode outcome => [outcome.Action],
            _ => [],
        };

        foreach (var reference in references)
        {
            if (reference?.Input is { } input && !input.IsAssignableFrom(trigger))
            {
                throw new InvalidOperationException(
                    $"Step '{reference}' takes {input.Name}, but the process starts on {trigger.Name}.");
            }
        }
    }

    private static InvalidOperationException Misplaced(string modifier, string target, ProcessNode node) =>
        new($"{modifier}() can only follow {target}, but it follows {node}.");
}
