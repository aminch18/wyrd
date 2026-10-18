using Shop.Contracts;
using Wyrd;

namespace Shop.Payments;

/// <summary>The boundary: the only place where an internal Payments fact becomes a public contract.</summary>
public sealed class PaymentChargedPublication : Publication<PaymentCharged, PaymentChargedV1>
{
    public override PaymentChargedV1 Map(PaymentCharged domainEvent) => new(domainEvent.OrderId, domainEvent.Amount, "EUR");
}
