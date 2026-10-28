using Wyrd;

namespace Telecom;

public sealed record PaymentBillingStatus(string SubscriptionId, bool Captured = false, string? Receipt = null, bool BillingStarted = false);

public sealed class PaymentBillingProjection : Projection<PaymentBillingStatus>
{
    public PaymentBillingProjection()
    {
        On<PaymentOpened>(e => e.Offer.SubscriptionId, (v, e) => v ?? new(e.Offer.SubscriptionId));
        On<PaymentCaptured>(e => e.Offer.SubscriptionId, (v, e) => (v ?? new(e.Offer.SubscriptionId)) with { Captured = true, Receipt = e.Receipt });
        On<BillingBegan>(e => e.SubscriptionId, (v, e) => (v ?? new(e.SubscriptionId)) with { BillingStarted = true });
    }
}
