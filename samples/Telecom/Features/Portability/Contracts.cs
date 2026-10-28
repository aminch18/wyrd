using Wyrd;

namespace Telecom;

[IntegrationEvent("telecom.mnp-confirmed", Version = 1)]
public sealed record PortConfirmedV1(string SubscriptionId, string Msisdn, long Epoch, DateTimeOffset WindowStart, DateTimeOffset WindowEnd) : IIntegrationEvent;
[IntegrationEvent("telecom.mnp-completed", Version = 1)]
public sealed record PortCompletedV1(string SubscriptionId, string Msisdn, long Epoch) : IIntegrationEvent;
[IntegrationEvent("telecom.mnp-cancelled", Version = 1)]
public sealed record PortCancelledV1(string SubscriptionId, string Msisdn, long Epoch) : IIntegrationEvent;
