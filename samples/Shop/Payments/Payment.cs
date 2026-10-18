using System.Text.Json.Serialization;
using Wyrd;

namespace Shop.Payments;

public sealed record PaymentCharged(string OrderId, decimal Amount) : IDomainEvent;

public sealed record PaymentRefunded(string OrderId, decimal Amount) : IDomainEvent;

public enum PaymentError
{
    CardDeclined,
    AlreadyCharged,
    NothingToRefund,
}

[Union]
[JsonDerivedType(typeof(None), "None")]
[JsonDerivedType(typeof(Charged), "Charged")]
[JsonDerivedType(typeof(Refunded), "Refunded")]
public abstract record Payment : IState<Payment>
{
    public static Payment Initial { get; } = new None();

    public Payment Evolve(IDomainEvent domainEvent) => (this, domainEvent) switch
    {
        (None, PaymentCharged charged) => new Charged(charged.Amount),
        (Charged, PaymentRefunded) => new Refunded(),
        _ => this,
    };

    public sealed record None : Payment;

    public sealed record Charged(decimal Amount) : Payment;

    public sealed record Refunded : Payment;
}
