using Wyrd;
using Wyrd.Processes;

namespace Telecom;

public static partial class Flows
{
    public static Process PaymentConfirmation => Process.StartOn<PaymentConfirmedV1>(e => e.SubscriptionId)
        .Then(ConfirmPayment).Retry(3.Times);

    private static RecordProviderCapture ConfirmPayment(PaymentConfirmedV1 e) => new(e.SubscriptionId, e.Receipt);
}
