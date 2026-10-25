using Wyrd;
using Wyrd.Processes;

namespace Telecom;

public static partial class Flows
{
    public static Process InitialProfile => Process.StartOn<PaymentCaptured>(e => e.Offer.SubscriptionId)
        .Then(Assign).Retry(3.Times);

    private static AssignProfile Assign(PaymentCaptured captured) => new(captured.Offer.SubscriptionId, captured.Offer.CustomerId, captured.Offer.ProfileId, 1);
}
