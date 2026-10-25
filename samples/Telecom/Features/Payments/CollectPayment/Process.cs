using Wyrd;
using Wyrd.Processes;

namespace Telecom;

public static partial class Flows
{
    public static Process PaymentCollection => Process.StartOn<PaymentOpened>(e => e.Offer.SubscriptionId)
        .Then(Capture).Retry(3.Times).NoCompensation("A possibly captured payment needs reconciliation, never a fictional void")
        .Then(StoreCapture).Retry(3.Times)
        .OnCompensated(PaymentIncident).Retry(3.Times);

    private static CapturePayment Capture(PaymentOpened opened) => new(opened.Offer);

    private static RecordCapture StoreCapture(PaymentOpened opened, CaptureReceived receipt) => new(opened.Offer, receipt.Receipt);

    private static OpenIncident PaymentIncident(PaymentOpened opened) => new(opened.Offer.SubscriptionId, "payment", 0);
}
