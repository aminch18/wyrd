using Wyrd;
using Wyrd.Processes;

namespace Telecom;

public static partial class Flows
{
    public static Process Contracting => Process.StartOn<SubscriptionOpened>(e => e.Offer.SubscriptionId)
        .Then(Claim).Retry(3.Times).Undo(Release)
        .Then(Admit).Retry(3.Times)
        .OnCompensated(Decline).Retry(3.Times);

    private static ClaimNumber Claim(SubscriptionOpened opened) => new(opened.Offer);

    private static ReleaseUnsubmittedNumber Release(SubscriptionOpened opened) => new(opened.Offer);

    private static AdmitSubscription Admit(SubscriptionOpened opened) => new(opened.Offer);

    private static DeclineSubscription Decline(SubscriptionOpened opened) => new(opened.Offer.SubscriptionId);
}
