using Wyrd;
using Wyrd.Processes;

namespace Telecom;

public static partial class Flows
{
    public static Process ProfileProvisioning => Process.StartOn<ProfileAssigned>(e => ProfileKey(e.SubscriptionId, e.ProfileId, e.Attempt))
        .Then(ClaimProfile).Retry(3.Times).NoCompensation("Keep the lease quarantined until external deactivation is verified")
        .Then(Provision).Retry(3.Times).NoCompensation("Provisioning may have succeeded even if its response was lost")
        .Then(StoreProvision).Retry(3.Times).NoCompensation("Provisioned is a fact, not verified connectivity")
        .Await<ConnectivityVerifiedV1>(e => ProfileKey(e.SubscriptionId, e.ProfileId, e.Attempt)).Within(30.Minutes)
        .Then(StoreConnectivity).Retry(3.Times)
        .OnCompensated(ActivationIncident).Retry(3.Times);

    private static ClaimProfile ClaimProfile(ProfileAssigned assigned) => new(assigned.SubscriptionId, assigned.CustomerId, assigned.ProfileId, assigned.Attempt);

    private static ProvisionProfile Provision(ProfileAssigned assigned) => new(assigned.SubscriptionId, assigned.CustomerId, assigned.ProfileId, assigned.Attempt);

    private static ObserveActivation StoreProvision(ProfileAssigned assigned) => new(assigned.SubscriptionId, ActivationFact.Provisioned, assigned.ProfileId, assigned.Attempt);

    private static ObserveActivation StoreConnectivity(ProfileAssigned assigned, ConnectivityVerifiedV1 verified) => new(assigned.SubscriptionId, ActivationFact.Connectivity, verified.ProfileId, verified.Attempt);

    private static OpenIncident ActivationIncident(ProfileAssigned assigned) => new(assigned.SubscriptionId, "connectivity", assigned.Attempt);
}
