using Wyrd;

namespace Telecom;

public sealed record Activation(
    bool Paid,
    bool Ported,
    string? CustomerId,
    string? ProfileId,
    int Attempt,
    bool Provisioned,
    bool Connected,
    bool Operational) : IState<Activation>
{
    public static Activation Initial { get; } = new(false, false, null, null, 0, false, false, false);

    public bool Ready => Paid && Ported && ProfileId is not null && Provisioned && Connected;

    public Activation Evolve(IDomainEvent domainEvent) => domainEvent switch
    {
        // Replacement invalidates both provisioning and connectivity evidence.
        ProfileAssigned assigned => this with
        {
            CustomerId = assigned.CustomerId,
            ProfileId = assigned.ProfileId,
            Attempt = assigned.Attempt,
            Provisioned = false,
            Connected = false,
        },
        ActivationObserved e => e.Next,
        ServiceOperational => this with { Operational = true },
        _ => this,
    };
}
