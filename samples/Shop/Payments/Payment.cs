using Wyrd;

namespace Shop.Payments;

public sealed record PaymentCharged(string OrderId, decimal Amount);

public sealed record PaymentRefunded(string OrderId, decimal Amount);

/// <summary>External event: the payment provider confirms the charge (e.g. after 3-D Secure).</summary>
public sealed record PaymentConfirmed(string OrderId);

public enum PaymentError
{
    CardDeclined,
    AlreadyCharged,
    NothingToRefund,
}

[Union]
public abstract record Payment : IState<Payment>
{
    public static Payment Initial { get; } = new None();

    public Payment Evolve(object domainEvent) => (this, domainEvent) switch
    {
        (None, PaymentCharged charged) => new Charged(charged.Amount),
        (Charged, PaymentRefunded) => new Refunded(),
        _ => this,
    };

    public sealed record None : Payment;

    public sealed record Charged(decimal Amount) : Payment;

    public sealed record Refunded : Payment;
}
