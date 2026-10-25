using Wyrd;
using Wyrd.Processes;

namespace Telecom;

public static partial class Flows
{
    public static Process PaymentReconciliation => Process.StartOn<PaymentReconciliationRequested>(e => $"{e.Offer.SubscriptionId}/{e.Review}")
        .Then(Find).Retry(3.Times).NoCompensation("Read-only provider inquiry")
        .Then(StoreReconciledCapture).Retry(3.Times)
        .OnCompensated(ReconciliationIncident).Retry(3.Times);

    private static FindPayment Find(PaymentReconciliationRequested requested) => new(requested.Offer);

    private static RecordCapture StoreReconciledCapture(PaymentReconciliationRequested requested, CaptureReceived receipt) => new(requested.Offer, receipt.Receipt);

    private static OpenIncident ReconciliationIncident(PaymentReconciliationRequested requested) => new(requested.Offer.SubscriptionId, "reconciliation", requested.Review);
}
