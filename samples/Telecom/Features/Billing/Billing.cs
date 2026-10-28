using Wyrd;

namespace Telecom;

public sealed record Billing(bool Started, string? ProfileId, int Attempt) : IState<Billing>
{
    public static Billing Initial { get; } = new(false, null, 0);
    public Billing Evolve(IDomainEvent domainEvent) => domainEvent switch
    {
        BillingBegan began => new(true, began.ProfileId, began.Attempt),
        _ => this,
    };
}
