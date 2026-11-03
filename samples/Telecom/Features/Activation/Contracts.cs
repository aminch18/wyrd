using Wyrd;

namespace Telecom;

[IntegrationEvent("telecom.service-operational", Version = 1)]
public sealed record ServiceOperationalV1(string SubscriptionId, string ProfileId, int Attempt) : IIntegrationEvent;

public sealed class OperationalPublication : Publication<ServiceOperational, ServiceOperationalV1>
{
    public override ServiceOperationalV1 Map(ServiceOperational domainEvent) =>
        new(domainEvent.SubscriptionId, domainEvent.ProfileId, domainEvent.Attempt);
}
