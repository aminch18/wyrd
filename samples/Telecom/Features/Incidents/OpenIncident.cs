using Wyrd;

namespace Telecom;

public sealed record OpenIncident(string SubscriptionId, string Kind, int Attempt) : ICommand;

public sealed record IncidentOpened(string SubscriptionId, string Kind, int Attempt) : IDomainEvent;

public sealed class OpenIncidentSlice : Slice<OpenIncident, Incident, IncidentOpened, RuleError>
{
    public override object KeyOf(OpenIncident command) => Incident.Key(command.SubscriptionId, command.Kind, command.Attempt);
    public override Result<IncidentOpened, RuleError> Decide(OpenIncident command, Incident state) =>
        new IncidentOpened(command.SubscriptionId, command.Kind, command.Attempt);
}
