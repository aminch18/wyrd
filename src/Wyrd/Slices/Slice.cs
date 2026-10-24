namespace Wyrd;

/// <summary>
/// A vertical slice: the single owner of a command. It decides, purely, whether the command is accepted
/// (producing a domain event) or rejected (producing an error). Wyrd loads and saves the state.
/// </summary>
public abstract class Slice<TCommand, TState, TEvent, TError> : ISliceHandler
    where TCommand : ICommand
    where TState : IState<TState>
    where TEvent : IDomainEvent
    where TError : notnull
{
    /// <summary>Which state this command decides on, e.g. the order id.</summary>
    public abstract object KeyOf(TCommand command);

    /// <summary>Purely decides whether a command produces a domain event or a business rejection against the current state.</summary>
    public abstract Result<TEvent, TError> Decide(TCommand command, TState state);

    Type ISliceHandler.Command => typeof(TCommand);

    Type ISliceHandler.State => typeof(TState);

    Type ISliceHandler.Event => typeof(TEvent);

    Type ISliceHandler.Error => typeof(TError);

    object ISliceHandler.Initial => TState.Initial;

    object ISliceHandler.KeyOf(ICommand command) => KeyOf((TCommand)command);

    Decision ISliceHandler.Decide(ICommand command, object state) =>
        Decide((TCommand)command, (TState)state).Match<Decision>(
            accepted => new Decision.Accepted(accepted),
            rejected => new Decision.Rejected(rejected));
}

internal interface ISliceHandler
{
    Type Command { get; }

    Type State { get; }

    Type Event { get; }

    Type Error { get; }

    object Initial { get; }

    object KeyOf(ICommand command);

    Decision Decide(ICommand command, object state);
}

/// <summary>The outcome of a command.</summary>
public abstract record Decision
{
    private Decision()
    {
    }

    /// <summary>A command accepted with the domain fact that evolves its state.</summary>
    /// <param name="Event">The accepted domain event.</param>
    public sealed record Accepted(IDomainEvent Event) : Decision;

    /// <summary>A command rejected by the business rule, without an accepted event or state change.</summary>
    /// <param name="Error">The domain error explaining the rejection.</param>
    public sealed record Rejected(object Error) : Decision;
}
