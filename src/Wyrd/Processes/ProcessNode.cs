namespace Wyrd.Processes;

public enum Backoff
{
    Immediate,
    Linear,
    Exponential,
}

public sealed record RetryPolicy(Attempts Attempts, Backoff Backoff)
{
    private static readonly TimeSpan MaxDelay = TimeSpan.FromMinutes(1);

    /// <summary>
    /// How undos and the outcome insist unless told otherwise: undoing is a guarantee, not an option.
    /// 10 retries, exponential, waits capped at a minute: about five minutes before Wyrd asks a human.
    /// </summary>
    public static RetryPolicy Undo { get; } = new(new Attempts(10), Backoff.Exponential);

    /// <summary>Delay before the given retry (1-based), capped at one minute.</summary>
    public TimeSpan DelayBefore(int retry)
    {
        var delay = Backoff switch
        {
            Backoff.Immediate => TimeSpan.Zero,
            Backoff.Linear => TimeSpan.FromSeconds(retry),
            _ => TimeSpan.FromSeconds(Math.Pow(2, Math.Min(retry - 1, 30))),
        };
        return delay < MaxDelay ? delay : MaxDelay;
    }
}

/// <summary>
/// A stable, human-readable reference to a step function, e.g. <c>Payment.Charge</c>.
/// Equality is by name only: two references to the same function are the same step.
/// </summary>
public sealed record StepRef(string Name)
{
    /// <summary>Builds the command from the trigger and, for each type in <see cref="Reads"/>, a fact of the instance.</summary>
    internal Func<object, Func<Type, object>, ICommand>? Invoke { get; init; }

    /// <summary>
    /// The events produced by earlier steps (or awaited) that the step reads besides the trigger, e.g.
    /// <c>Ship(OrderPlaced order, PaymentCharged charged)</c> reads <c>PaymentCharged</c>.
    /// </summary>
    public IReadOnlyList<Type> Reads { get; init; } = [];

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
        From(step) with { Input = typeof(TIn), Output = typeof(TCommand), Invoke = (input, _) => step((TIn)input) };

    /// <summary>
    /// A step given as a plain delegate (pipe syntax). It is bound to a typed call once the process trigger is known:
    /// see <see cref="Bind{TTrigger}"/>.
    /// </summary>
    internal static StepRef Untyped(Delegate step)
    {
        var reference = From(step);
        var parameters = step.Method.GetParameters();
        if (parameters.Length == 0 || !typeof(ICommand).IsAssignableFrom(step.Method.ReturnType)
            || parameters.Skip(1).Any(p => !typeof(IEvent).IsAssignableFrom(p.ParameterType)))
        {
            throw new InvalidOperationException(
                $"Step '{reference}' must take the trigger, then optionally events of earlier steps, and return an ICommand.");
        }

        return reference with
        {
            Input = parameters[0].ParameterType,
            Output = step.Method.ReturnType,
            Reads = [.. parameters.Skip(1).Select(p => p.ParameterType)],
            Raw = step,
        };
    }

    /// <summary>The delegate of a step not bound yet (pipe syntax).</summary>
    internal Delegate? Raw { get; init; }

    /// <summary>
    /// Turns a plain delegate into a typed call, without reflection: delegate variance makes a
    /// <c>Func&lt;OrderPlaced, ReserveStock&gt;</c> a <c>Func&lt;OrderPlaced, ICommand&gt;</c>.
    /// </summary>
    internal StepRef Bind<TTrigger>()
    {
        if (Invoke is not null || Raw is null)
        {
            return this;
        }

        if (Reads.Count > 0)
        {
            // Reading events: the types are only known from the delegate, so it is invoked dynamically (no code generation).
            var raw = Raw;
            var reads = Reads;
            return this with { Invoke = (input, fact) => Call(raw, [input, .. reads.Select(fact)]), Raw = null };
        }

        return Raw is Func<TTrigger, ICommand> step
            ? this with { Invoke = (input, _) => step((TTrigger)input), Raw = null }
            : throw new InvalidOperationException(
                $"Step '{Name}' can't be called with {typeof(TTrigger).Name}: it must take {typeof(TTrigger).Name} (or a type it derives from) and return a command record.");
    }

    private static ICommand Call(Delegate step, object[] arguments)
    {
        try
        {
            return (ICommand)step.DynamicInvoke(arguments)!;
        }
        catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(ex.InnerException);
            throw;
        }
    }

    public bool Equals(StepRef? other) => other is not null && Name == other.Name;

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Name);

    public override string ToString() => Name;
}

public abstract record ProcessNode;

/// <summary>
/// A step: a command, how it retries, and how it is undone. <c>Retry</c> is the step's own decision (none unless declared);
/// the undo always insists, with <c>CompensationRetry</c> or, when null, <see cref="RetryPolicy.Undo"/>.
/// </summary>
public sealed record StepNode(StepRef Action, StepRef? Compensation = null, RetryPolicy? Retry = null) : ProcessNode
{
    /// <summary>Why this step needs no undo, when that was decided explicitly with <c>.NoCompensation("why")</c>.</summary>
    public string? NoCompensationReason { get; init; }

    /// <summary>Why this step is not retried, when that was decided explicitly with <c>.NoRetry("why")</c>.</summary>
    public string? NoRetryReason { get; init; }

    /// <summary>When the step's retries are exhausted: compensate automatically instead of suspending.</summary>
    public bool CompensateWhenExhausted { get; init; }

    /// <summary>How the undo insists, when adjusted; null means <see cref="RetryPolicy.Undo"/>.</summary>
    public RetryPolicy? CompensationRetry { get; init; }

    /// <summary>The policy the undo actually uses.</summary>
    public RetryPolicy UndoPolicy => CompensationRetry ?? RetryPolicy.Undo;
}

/// <summary>
/// How the process ends when it is compensated: a command that tells the business (e.g. <c>CancelOrder</c>),
/// so read models and other slices learn the outcome. Not a position in the flow.
/// </summary>
public sealed record CompensatedNode(StepRef Action, RetryPolicy? Retry = null) : ProcessNode
{
    /// <summary>The outcome is part of undoing: it insists like an undo unless adjusted.</summary>
    public RetryPolicy Policy => Retry ?? RetryPolicy.Undo;
}

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
    public static ProcessNode Undo(ProcessNode node, StepRef compensation, RetryPolicy? insist) => node switch
    {
        StepNode { NoCompensationReason: not null } => throw new InvalidOperationException(
            "A step is either undone with Undo() or explicitly not with NoCompensation(), never both."),
        StepNode step => step with { Compensation = compensation, CompensationRetry = insist },
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

    public static ProcessNode NoRetry(ProcessNode node, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return node switch
        {
            StepNode { Retry: not null } => throw new InvalidOperationException(
                "A step either retries with Retry() or explicitly not with NoRetry(), never both."),
            StepNode step => step with { NoRetryReason = reason },
            _ => throw Misplaced(nameof(NoRetry), "a step", node),
        };
    }

    public static ProcessNode OrCompensate(ProcessNode node) => node switch
    {
        StepNode step => step with { CompensateWhenExhausted = true },
        _ => throw Misplaced(nameof(OrCompensate), "a step", node),
    };

    public static ProcessNode Retry(ProcessNode node, Attempts attempts, Backoff backoff) => node switch
    {
        StepNode { NoRetryReason: not null } => throw new InvalidOperationException(
            "A step either retries with Retry() or explicitly not with NoRetry(), never both."),
        StepNode step => step with { Retry = new RetryPolicy(attempts, backoff) },
        CompensatedNode outcome => outcome with { Retry = new RetryPolicy(attempts, backoff) },
        _ => throw Misplaced(nameof(Retry), "a step or OnCompensated", node),
    };

    public static ProcessNode Within(ProcessNode node, TimeSpan timeout) => node switch
    {
        AwaitNode wait => wait with { Timeout = timeout },
        _ => throw Misplaced(nameof(Within), "an Await", node),
    };

    /// <summary>Binds the steps of a node given as plain delegates to typed calls for this trigger.</summary>
    public static ProcessNode Bind<TTrigger>(ProcessNode node) => node switch
    {
        StepNode step => step with { Action = step.Action.Bind<TTrigger>(), Compensation = step.Compensation?.Bind<TTrigger>() },
        CompensatedNode outcome => outcome with { Action = outcome.Action.Bind<TTrigger>() },
        _ => node,
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
