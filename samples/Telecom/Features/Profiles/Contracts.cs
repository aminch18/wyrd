using Wyrd;

namespace Telecom;

[IntegrationEvent("telecom.connectivity-verified", Version = 1)]
public sealed record ConnectivityVerifiedV1(string SubscriptionId, string ProfileId, int Attempt) : IIntegrationEvent;
