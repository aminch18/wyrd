using Wyrd;

namespace Telecom;

[IntegrationEvent("telecom.payment-confirmed", Version = 1)]
public sealed record PaymentConfirmedV1(string SubscriptionId, string Receipt) : IIntegrationEvent;

[IntegrationEvent("telecom.mnp-confirmed", Version = 1)]
public sealed record PortConfirmedV1(string SubscriptionId, string Msisdn, long Epoch, DateTimeOffset WindowStart, DateTimeOffset WindowEnd) : IIntegrationEvent;
[IntegrationEvent("telecom.mnp-completed", Version = 1)]
public sealed record PortCompletedV1(string SubscriptionId, string Msisdn, long Epoch) : IIntegrationEvent;
[IntegrationEvent("telecom.mnp-cancelled", Version = 1)]
public sealed record PortCancelledV1(string SubscriptionId, string Msisdn, long Epoch) : IIntegrationEvent;
[IntegrationEvent("telecom.connectivity-verified", Version = 1)]
public sealed record ConnectivityVerifiedV1(string SubscriptionId, string ProfileId, int Attempt) : IIntegrationEvent;
[IntegrationEvent("telecom.service-operational", Version = 1)]
public sealed record ServiceOperationalV1(string SubscriptionId, string ProfileId, int Attempt) : IIntegrationEvent;

public sealed class OperationalPublication : Publication<ServiceOperational, ServiceOperationalV1>
{
    public override ServiceOperationalV1 Map(ServiceOperational domainEvent) =>
        new(domainEvent.SubscriptionId, domainEvent.ProfileId, domainEvent.Attempt);
}
