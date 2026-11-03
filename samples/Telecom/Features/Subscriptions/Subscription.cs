using System.Text.Json.Serialization;
using Wyrd;

namespace Telecom;

[Union]
[JsonDerivedType(typeof(Empty), "empty")]
[JsonDerivedType(typeof(Contracted), "contracted")]
[JsonDerivedType(typeof(Declined), "declined")]
public abstract record Subscription : IState<Subscription>
{
    public static Subscription Initial { get; } = new Empty();
    public Subscription Evolve(IDomainEvent domainEvent) => (this, domainEvent) switch
    {
        (Empty, SubscriptionOpened e) => new Contracted(e.Offer),
        (Contracted, SubscriptionDeclined) => new Declined(),
        _ => this,
    };
    public sealed record Empty : Subscription;
    public sealed record Contracted(Offer Offer) : Subscription;
    public sealed record Declined : Subscription;
}
