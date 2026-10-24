namespace Wyrd;

/// <summary>
/// A read model: a view shaped for a screen, a list or a report, kept up to date from events with pure functions.
/// Wyrd stores it in the same transaction that handles the event, and never applies the same message twice.
/// </summary>
/// <example>
/// <code>
/// public sealed class OrderSummaries : Projection&lt;OrderSummary&gt;
/// {
///     public OrderSummaries()
///     {
///         On&lt;OrderPlaced&gt;(e => e.OrderId, (_, e) => new OrderSummary(e.OrderId, e.Amount, "Placed"));
///         On&lt;OrderShipped&gt;(e => e.OrderId, (view, e) => view! with { Status = "Shipped" });
///     }
/// }
/// </code>
/// </example>
public abstract class Projection<TView> : IProjectionHandler
    where TView : class
{
    private readonly Dictionary<Type, (Func<IEvent, object> Key, Func<object?, IEvent, object?> Apply)> _handlers = [];
    private readonly Dictionary<Type, (Func<string, object, object> Key, Func<object?, string, object, object?> Seed)> _seeds = [];

    Type IProjectionHandler.View => typeof(TView);

    IEnumerable<Type> IProjectionHandler.Events => _handlers.Keys;

    IEnumerable<Type> IProjectionHandler.Seeds => _seeds.Keys;

    /// <summary>
    /// Handles an event: <paramref name="key"/> picks the view to update; <paramref name="apply"/> returns the
    /// new view from the current one (null if it doesn't exist yet). Return null to delete the view.
    /// </summary>
    protected void On<TEvent>(Func<TEvent, object> key, Func<TView?, TEvent, TView?> apply)
        where TEvent : IEvent
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(apply);
        _handlers[typeof(TEvent)] = (e => key((TEvent)e), (view, e) => apply((TView?)view, (TEvent)e));
    }

    /// <summary>
    /// Catches the view up from states that already exist, for a projection added to a store that has data. Wyrd keeps
    /// the current state, not every past event, so a new projection starts from the states of
    /// <typeparamref name="TState"/>, once, and from their events after that. <paramref name="key"/> picks the view
    /// from the state's key and the state; <paramref name="seed"/> returns the view from the current one (null if it
    /// doesn't exist yet), the state's key and the state. Return null to leave no view. Only what a state says can be
    /// rebuilt: a view that counts past events starts from what the states show.
    /// </summary>
    /// <example>
    /// <code>
    /// Seed&lt;Order&gt;((orderId, _) => orderId, (_, orderId, order) => new OrderSummary(orderId, order.Amount, order.Status));
    /// </code>
    /// </example>
    protected void Seed<TState>(Func<string, TState, object> key, Func<TView?, string, TState, TView?> seed)
        where TState : IState<TState>
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(seed);
        _seeds[typeof(TState)] = ((k, s) => key(k, (TState)s), (view, k, s) => seed((TView?)view, k, (TState)s));
    }

    object IProjectionHandler.KeyOf(IEvent @event) => _handlers[@event.GetType()].Key(@event);

    object? IProjectionHandler.Apply(object? view, IEvent @event) => _handlers[@event.GetType()].Apply(view, @event);

    object IProjectionHandler.SeedKeyOf(string key, object state) => _seeds[state.GetType()].Key(key, state);

    object? IProjectionHandler.Seed(object? view, string key, object state) => _seeds[state.GetType()].Seed(view, key, state);
}

internal interface IProjectionHandler
{
    Type View { get; }

    IEnumerable<Type> Events { get; }

    /// <summary>The state types the projection catches up from (<see cref="Projection{TView}.Seed{TState}"/>).</summary>
    IEnumerable<Type> Seeds { get; }

    object KeyOf(IEvent @event);

    object? Apply(object? view, IEvent @event);

    object SeedKeyOf(string key, object state);

    object? Seed(object? view, string key, object state);
}
