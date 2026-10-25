using Wyrd;
using Wyrd.Processes;

namespace Telecom;

public static partial class Flows
{
    public static Process PortSubmission => Process.StartOn<PaymentCaptured>(e => PortKey(e.Offer.SubscriptionId, e.Offer.Msisdn, e.Offer.Epoch))
        .Then(Submit).Retry(3.Times).NoCompensation("Request cancellation is a separate protocol; completion cannot be reversed")
        .Then(StoreSubmission).Retry(3.Times).NoCompensation("Submission is a fact")
        .Await<PortCompletedV1>(e => PortKey(e.SubscriptionId, e.Msisdn, e.Epoch)).Within(2.Days)
        .OnCompensated(PortIncident).Retry(3.Times);

    private static SubmitPort Submit(PaymentCaptured captured) => new(captured.Offer);

    private static MarkPortSubmitted StoreSubmission(PaymentCaptured captured) => new(captured.Offer);

    private static OpenIncident PortIncident(PaymentCaptured captured) => new(captured.Offer.SubscriptionId, "port-deadline", 0);
}
