using Wyrd;
using Wyrd.Processes;

namespace Telecom;

public static partial class Flows
{
    public static Process BillingCommencement => Process.StartOn<ServiceOperationalV1>(e => e.SubscriptionId)
        .Then(Bill).Retry(3.Times);

    private static BeginBilling Bill(ServiceOperationalV1 operational) => new(operational.SubscriptionId, operational.ProfileId, operational.Attempt);
}
