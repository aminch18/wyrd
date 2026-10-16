namespace Wyrd.Testing;

/// <summary>
/// Given/When/Then for Wyrd. Scenarios are pure: no runtime, no infrastructure, they run in microseconds.
/// </summary>
/// <example>
/// <code>
/// Scenario.For(new ChargePaymentSlice())
///     .Given(new PaymentCharged("A-1", 89))
///     .When(new ChargePayment("A-1", 89))
///     .ThenRejected(PaymentError.AlreadyCharged);
/// </code>
/// </example>
public static class Scenario
{
    public static SliceScenario<TCommand, TState, TEvent, TError> For<TCommand, TState, TEvent, TError>(
        Slice<TCommand, TState, TEvent, TError> slice)
        where TCommand : ICommand
        where TState : IState<TState>
        where TEvent : IDomainEvent
        where TError : notnull
    {
        ArgumentNullException.ThrowIfNull(slice);
        return new(slice, TState.Initial);
    }

    public static ProjectionScenario<TView> For<TView>(Projection<TView> projection)
        where TView : class
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new(projection, []);
    }
}
