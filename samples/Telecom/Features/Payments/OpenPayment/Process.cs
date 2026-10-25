using Wyrd;
using Wyrd.Processes;

namespace Telecom;

public static partial class Flows
{
    public static Process PaymentOpening => Process.StartOn<SubscriptionAdmitted>(e => e.Offer.SubscriptionId)
        .Then(OpenPayment).Retry(3.Times);

    private static OpenPayment OpenPayment(SubscriptionAdmitted admitted) => new(admitted.Offer);
}
