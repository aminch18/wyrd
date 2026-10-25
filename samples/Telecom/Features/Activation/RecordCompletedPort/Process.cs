using Wyrd;
using Wyrd.Processes;

namespace Telecom;

public static partial class Flows
{
    public static Process PortEvidence => Process.StartOn<NumberChanged>(e => $"{e.Offer.SubscriptionId}/{e.Phase}")
        .Then(ObservePort).Retry(3.Times);

    private static RecordCompletedPort ObservePort(NumberChanged changed) => new(changed.Offer.SubscriptionId, changed.Phase);
}
