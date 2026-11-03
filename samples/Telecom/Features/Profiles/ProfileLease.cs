using Wyrd;

namespace Telecom;

// A single resource owner. Profiles are quarantined after replacement; reuse needs a real deactivation protocol.
public sealed record ProfileLease(string? SubscriptionId, string? CustomerId, int Attempt) : IState<ProfileLease>
{
    public static ProfileLease Initial { get; } = new(null, null, 0);
    public ProfileLease Evolve(IDomainEvent domainEvent) => domainEvent switch
    {
        ProfileClaimed claimed => new(claimed.SubscriptionId, claimed.CustomerId, claimed.Attempt),
        _ => this,
    };
}
