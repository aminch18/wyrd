namespace Wyrd.Testing;

/// <summary>Given what already happened, when a command arrives, then the slice decides...</summary>
public sealed class SliceScenario<TCommand, TState, TEvent, TError>
    where TCommand : ICommand
    where TState : IState<TState>
    where TEvent : IDomainEvent
    where TError : notnull
{
    private readonly Slice<TCommand, TState, TEvent, TError> _slice;
    private readonly TState _state;

    internal SliceScenario(Slice<TCommand, TState, TEvent, TError> slice, TState state)
    {
        _slice = slice;
        _state = state;
    }

    /// <summary>What already happened: the state is rebuilt from these events with <c>Evolve</c>.</summary>
    public SliceScenario<TCommand, TState, TEvent, TError> Given(params IDomainEvent[] history)
    {
        ArgumentNullException.ThrowIfNull(history);
        return new(_slice, history.Aggregate(_state, (state, @event) => state.Evolve(@event)));
    }

    /// <summary>Start from an explicit state instead of a history.</summary>
    public SliceScenario<TCommand, TState, TEvent, TError> GivenState(TState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return new(_slice, state);
    }

    /// <summary>Checks that the command survives JSON serialization, then runs the slice's pure decision against the given state.</summary>
    public SliceOutcome<TCommand, TState, TEvent, TError> When(TCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        Json.EnsureRoundTrips(command, typeof(TCommand), "Command");
        return new(command, _state, _slice.Decide(command, _state));
    }
}

/// <summary>...the slice accepts with an event or rejects with an error.</summary>
public sealed class SliceOutcome<TCommand, TState, TEvent, TError>
    where TCommand : ICommand
    where TState : IState<TState>
    where TEvent : IDomainEvent
    where TError : notnull
{
    private readonly TCommand _command;
    private readonly TState _before;
    private readonly Result<TEvent, TError> _result;

    internal SliceOutcome(TCommand command, TState before, Result<TEvent, TError> result)
    {
        _command = command;
        _before = before;
        _result = result;
    }

    /// <summary>The command is accepted and produces exactly this event (compared as stored, by JSON).</summary>
    public SliceOutcome<TCommand, TState, TEvent, TError> Then(TEvent expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var actual = Accepted($"to be accepted with {expected}");
        if (!Json.SameAs(expected, actual))
        {
            throw new WyrdAssertionException($"Expected {Name} to produce {expected}, but it produced {actual}.");
        }

        return this;
    }

    /// <summary>The command is rejected with exactly this error.</summary>
    public SliceOutcome<TCommand, TState, TEvent, TError> ThenRejected(TError expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var actual = _result.Match<object>(
            accepted => throw new WyrdAssertionException($"Expected {Name} to be rejected with {expected}, but it was accepted: {accepted}."),
            error => error);

        if (!Equals(expected, actual))
        {
            throw new WyrdAssertionException($"Expected {Name} to be rejected with {expected}, but it was rejected with {actual}.");
        }

        return this;
    }

    /// <summary>After the accepted event, the state is exactly this (compared as stored, by JSON).</summary>
    public SliceOutcome<TCommand, TState, TEvent, TError> ThenState(TState expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var after = _before.Evolve(Accepted("to be accepted, to check the resulting state"));
        if (!Json.SameAs(expected, after))
        {
            throw new WyrdAssertionException($"Expected the state after {Name} to be {expected}, but it is {after}.");
        }

        return this;
    }

    private static string Name => typeof(TCommand).Name;

    private TEvent Accepted(string expectation)
    {
        var @event = _result.Match(
            accepted => accepted,
            error => throw new WyrdAssertionException($"Expected {Name} {expectation}, but it was rejected with {error}."));

        Json.EnsureRoundTrips(@event, typeof(TEvent), "Event");
        Json.EnsureRoundTrips(_before.Evolve(@event), typeof(TState), "State");
        return @event;
    }
}
