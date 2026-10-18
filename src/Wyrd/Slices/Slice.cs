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

    public abstract Result<TEvent, TError> Decide(TCommand command, TState state);

    Type ICommandOwner.Command => typeof(TCommand);

    Type ISliceHandler.State => typeof(TState);

    Type ICommandOwner.Event => typeof(TEvent);

    Type ICommandOwner.Error => typeof(TError);

    object ISliceHandler.Initial => TState.Initial;

    object ICommandOwner.KeyOf(ICommand command) => KeyOf((TCommand)command);

    Decision ISliceHandler.Decide(ICommand command, object state) =>
        Decide((TCommand)command, (TState)state).Match<Decision>(
            accepted => new Decision.Accepted(accepted),
            rejected => new Decision.Rejected(rejected));
}

internal interface ISliceHandler : ICommandOwner
{
    Type State { get; }

    object Initial { get; }

    Decision Decide(ICommand command, object state);
}

/// <summary>The outcome of a command.</summary>
public abstract record Decision
{
    private Decision()
    {
    }

    public sealed record Accepted(IDomainEvent Event) : Decision;

    public sealed record Rejected(object Error) : Decision;
}
