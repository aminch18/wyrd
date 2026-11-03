using Wyrd;

namespace Telecom;

public sealed record IncidentItem(string SubscriptionId, string Kind, int Attempt, bool Open);

public sealed class IncidentProjection : Projection<IncidentItem>
{
    public IncidentProjection()
    {
        On<IncidentOpened>(e => Incident.Key(e.SubscriptionId, e.Kind, e.Attempt), (_, e) => new(e.SubscriptionId, e.Kind, e.Attempt, true));
        On<IncidentResolved>(e => Incident.Key(e.SubscriptionId, e.Kind, e.Attempt), (_, e) => new(e.SubscriptionId, e.Kind, e.Attempt, false));
    }
}
