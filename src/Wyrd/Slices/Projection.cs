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

    Type IProjectionHandler.View => typeof(TView);

    IEnumerable<Type> IProjectionHandler.Events => _handlers.Keys;

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

    object IProjectionHandler.KeyOf(IEvent @event) => _handlers[@event.GetType()].Key(@event);

    object? IProjectionHandler.Apply(object? view, IEvent @event) => _handlers[@event.GetType()].Apply(view, @event);
}

internal interface IProjectionHandler
{
    Type View { get; }

    IEnumerable<Type> Events { get; }

    object KeyOf(IEvent @event);

    object? Apply(object? view, IEvent @event);
}
