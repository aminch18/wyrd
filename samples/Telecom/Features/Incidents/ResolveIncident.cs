using Wyrd;

namespace Telecom;

public sealed record ResolveIncident(string SubscriptionId, string Kind, int Attempt) : ICommand;

public sealed record IncidentResolved(string SubscriptionId, string Kind, int Attempt) : IDomainEvent;

public sealed class ResolveIncidentSlice : Slice<ResolveIncident, Incident, IncidentResolved, RuleError>
{
    public override object KeyOf(ResolveIncident command) => Incident.Key(command.SubscriptionId, command.Kind, command.Attempt);
    public override Result<IncidentResolved, RuleError> Decide(ResolveIncident command, Incident state) =>
        new IncidentResolved(command.SubscriptionId, command.Kind, command.Attempt);
}
