namespace Wyrd;

/// <summary>
/// A command is the entry point of a vertical slice: it carries its data and its pure <c>Decide</c> function.
/// The runtime executes it with no registration and no reflection.
/// </summary>
public interface ICommand
{
    /// <summary>Identifies the state (stream) this command decides on, e.g. an order id.</summary>
    object Key { get; }

    internal Type StateType { get; }

    internal object InitialState { get; }

    internal Decision Execute(object state);
}

/// <inheritdoc cref="ICommand"/>
public interface ICommand<TSelf, TState, TEvent, TError> : ICommand
    where TSelf : ICommand<TSelf, TState, TEvent, TError>
    where TState : IState<TState>
    where TEvent : notnull
    where TError : notnull
{
    static abstract Result<TEvent, TError> Decide(TSelf command, TState state);

    Type ICommand.StateType => typeof(TState);

    object ICommand.InitialState => TState.Initial;

    Decision ICommand.Execute(object state) =>
        TSelf.Decide((TSelf)this, (TState)state).Match<Decision>(
            ok => new Decision.Accepted(ok),
            error => new Decision.Rejected(error));
}

/// <summary>State rebuilt from events. <c>Evolve</c> must be pure.</summary>
public interface IState
{
    internal object Apply(object domainEvent);
}

/// <inheritdoc cref="IState"/>
public interface IState<TSelf> : IState
    where TSelf : IState<TSelf>
{
    static abstract TSelf Initial { get; }

    TSelf Evolve(object domainEvent);

    object IState.Apply(object domainEvent) => Evolve(domainEvent);
}

/// <summary>The outcome of executing a command.</summary>
public abstract record Decision
{
    private Decision()
    {
    }

    public sealed record Accepted(object Event) : Decision;

    public sealed record Rejected(object Error) : Decision;
}
