using Wyrd;

namespace Telecom;

[IntegrationEvent("telecom.payment-confirmed", Version = 1)]
public sealed record PaymentConfirmedV1(string SubscriptionId, string Receipt) : IIntegrationEvent;
