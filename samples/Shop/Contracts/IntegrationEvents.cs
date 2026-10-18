using Wyrd;

namespace Shop.Contracts;

// Public, versioned contracts. Other services may depend on these, and only on these.

/// <summary>Published by the payment provider's webhook.</summary>
[IntegrationEvent("psp.payment-confirmed")]
public sealed record PaymentConfirmed(string OrderId) : IIntegrationEvent;

[IntegrationEvent("payments.payment-charged", Version = 1)]
public sealed record PaymentChargedV1(string OrderId, decimal Amount, string Currency) : IIntegrationEvent;

[IntegrationEvent("shipping.order-shipped", Version = 1)]
public sealed record OrderShippedV1(string OrderId, DateOnly ShippedOn) : IIntegrationEvent;
