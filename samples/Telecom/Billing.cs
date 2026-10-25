using Wyrd;

namespace Telecom;

public sealed record BeginBilling(string SubscriptionId, string ProfileId, int Attempt) : ICommand;
public sealed record BillingBegan(string SubscriptionId, string ProfileId, int Attempt) : IDomainEvent;
public sealed record Billing(bool Started, string? ProfileId, int Attempt) : IState<Billing>
{
    public static Billing Initial { get; } = new(false, null, 0);
    public Billing Evolve(IDomainEvent domainEvent) => domainEvent is BillingBegan e ? new(true, e.ProfileId, e.Attempt) : this;
}
public sealed class BeginBillingSlice : Slice<BeginBilling, Billing, BillingBegan, RuleError>
{
    public override object KeyOf(BeginBilling command) => command.SubscriptionId;
    public override Result<BillingBegan, RuleError> Decide(BeginBilling command, Billing state) => state.Started
        ? RuleError.AlreadyExists : new BillingBegan(command.SubscriptionId, command.ProfileId, command.Attempt);
}

public sealed record OpenIncident(string SubscriptionId, string Kind, int Attempt) : ICommand;
public sealed record IncidentOpened(string SubscriptionId, string Kind, int Attempt) : IDomainEvent;
public sealed record ResolveIncident(string SubscriptionId, string Kind, int Attempt) : ICommand;
public sealed record IncidentResolved(string SubscriptionId, string Kind, int Attempt) : IDomainEvent;
public sealed record Incident(bool Open) : IState<Incident>
{
    public static Incident Initial { get; } = new(false);
    public Incident Evolve(IDomainEvent domainEvent) => domainEvent switch
    {
        IncidentOpened => new(true), IncidentResolved => new(false), _ => this,
    };
    public static string Key(string id, string kind, int attempt) => $"{id}/{kind}/{attempt}";
}
public sealed class OpenIncidentSlice : Slice<OpenIncident, Incident, IncidentOpened, RuleError>
{
    public override object KeyOf(OpenIncident command) => Incident.Key(command.SubscriptionId, command.Kind, command.Attempt);
    public override Result<IncidentOpened, RuleError> Decide(OpenIncident command, Incident state) =>
        new IncidentOpened(command.SubscriptionId, command.Kind, command.Attempt);
}
public sealed class ResolveIncidentSlice : Slice<ResolveIncident, Incident, IncidentResolved, RuleError>
{
    public override object KeyOf(ResolveIncident command) => Incident.Key(command.SubscriptionId, command.Kind, command.Attempt);
    public override Result<IncidentResolved, RuleError> Decide(ResolveIncident command, Incident state) =>
        new IncidentResolved(command.SubscriptionId, command.Kind, command.Attempt);
}
