using Wyrd;
using Wyrd.Processes;

namespace Telecom;

public static partial class Flows
{
    public static Process PortCancellation => Process.StartOn<PortCancellationRequested>(e => $"{e.Offer.SubscriptionId}/{e.Offer.Epoch}")
        .Then(CancelPort).Retry(3.Times);

    private static CancelPort CancelPort(PortCancellationRequested requested) => new(requested.Offer);
}
