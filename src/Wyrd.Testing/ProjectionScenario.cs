namespace Wyrd.Testing;

/// <summary>Given these events, then the read model shows...</summary>
public sealed class ProjectionScenario<TView>
    where TView : class
{
    private readonly Projection<TView> _projection;
    private readonly Dictionary<string, TView> _views;

    internal ProjectionScenario(Projection<TView> projection, Dictionary<string, TView> views)
    {
        _projection = projection;
        _views = views;
    }

    /// <summary>Events the projection receives, in order. Events it doesn't handle are ignored, as in production.</summary>
    public ProjectionScenario<TView> Given(params IEvent[] events)
    {
        ArgumentNullException.ThrowIfNull(events);
        IProjectionHandler handler = _projection;
        var views = new Dictionary<string, TView>(_views);

        foreach (var @event in events.Where(e => handler.Events.Contains(e.GetType())))
        {
            var key = Runtime.Keys.Of(handler.KeyOf(@event));
            var next = (TView?)handler.Apply(views.GetValueOrDefault(key), @event);
            if (next is null)
            {
                views.Remove(key);
            }
            else
            {
                Json.EnsureRoundTrips(next, typeof(TView), "View");
                views[key] = next;
            }
        }

        return new(_projection, views);
    }

    /// <summary>The view for <paramref name="key"/> is exactly this (compared as stored), or doesn't exist when null.</summary>
    public ProjectionScenario<TView> Then(object key, TView? expected)
    {
        ArgumentNullException.ThrowIfNull(key);
        var actual = _views.GetValueOrDefault(Runtime.Keys.Of(key));
        if (!Json.SameAs(expected, actual))
        {
            throw new WyrdAssertionException(
                $"Expected {typeof(TView).Name} '{key}' to be {expected?.ToString() ?? "absent"}, but it is {actual?.ToString() ?? "absent"}.");
        }

        return this;
    }
}
