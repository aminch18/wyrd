using Wyrd;
using Wyrd.Processes;

namespace Telecom;

public static partial class Flows
{
    public static Process PaymentEvidence => Process.StartOn<PaymentCaptured>(e => e.Offer.SubscriptionId)
        .Then(ObservePayment).Retry(3.Times);

    private static ObserveActivation ObservePayment(PaymentCaptured captured) => new(captured.Offer.SubscriptionId, ActivationFact.Payment);
}
