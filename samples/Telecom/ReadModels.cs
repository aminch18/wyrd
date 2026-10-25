using Wyrd;

namespace Telecom;

public sealed record CustomerProgress(string SubscriptionId, string? Msisdn = null, bool Paid = false, bool Confirmed = false,
    bool Ported = false, int Attempt = 0, string? ProfileId = null, bool Provisioned = false, bool Connected = false, bool Operational = false,
    bool CancellationRequested = false, bool Cancelled = false, bool Declined = false)
{
    public string Stage => Operational ? "Operational" : Ported && !Connected ? "Ported: connectivity pending"
        : Ported ? "Activation pending" : Cancelled ? "Port cancelled" : CancellationRequested ? "Cancellation requested"
        : Declined ? "Subscription declined" : Confirmed ? "Porting window agreed" : Paid ? "Port requested" : "Payment pending";
}
public sealed class CustomerProgressProjection : Projection<CustomerProgress>
{
    public CustomerProgressProjection()
    {
        On<SubscriptionOpened>(e => e.Offer.SubscriptionId, (v, e) => (v ?? new(e.Offer.SubscriptionId)) with { Msisdn = e.Offer.Msisdn });
        On<SubscriptionDeclined>(e => e.SubscriptionId, (v, e) => (v ?? new(e.SubscriptionId)) with { Declined = true });
        On<NumberReservationReleased>(e => e.Offer.SubscriptionId, (v, e) => (v ?? new(e.Offer.SubscriptionId)) with { Cancelled = (v?.Cancelled ?? false) || e.Released });
        On<PortCancellationRequested>(e => e.Offer.SubscriptionId, (v, e) => (v ?? new(e.Offer.SubscriptionId)) with { CancellationRequested = true });
        On<PaymentCaptured>(e => e.Offer.SubscriptionId, (v, e) => (v ?? new(e.Offer.SubscriptionId)) with { Paid = true });
        On<NumberChanged>(e => e.Offer.SubscriptionId, (v, e) => (v ?? new(e.Offer.SubscriptionId)) with
        {
            Confirmed = (v?.Confirmed ?? false) || e.Phase == PortPhase.Confirmed,
            Ported = (v?.Ported ?? false) || e.Phase == PortPhase.Completed,
            Cancelled = (v?.Cancelled ?? false) || e.Phase == PortPhase.Cancelled,
        });
        On<ProfileAssigned>(e => e.SubscriptionId, (v, e) => e.Attempt <= (v?.Attempt ?? 0) ? v
            : (v ?? new(e.SubscriptionId)) with { Attempt = e.Attempt, ProfileId = e.ProfileId, Provisioned = false, Connected = false });
        On<ActivationObserved>(e => e.SubscriptionId, (v, e) => (v ?? new(e.SubscriptionId)) with
        {
            Paid = (v?.Paid ?? false) || e.Next.Paid,
            Ported = (v?.Ported ?? false) || e.Next.Ported,
            Attempt = e.Next.Attempt, ProfileId = e.Next.ProfileId,
            Provisioned = e.Next.Provisioned, Connected = e.Next.Connected,
        });
        On<ServiceOperational>(e => e.SubscriptionId, (v, e) => (v ?? new(e.SubscriptionId)) with { Operational = true });
    }
}

public sealed record PortabilityDetail(string SubscriptionId, string Msisdn, long Epoch, PortPhase Phase,
    DateTimeOffset WindowStart, DateTimeOffset WindowEnd, bool CancellationRequested);
public sealed class PortabilityDetailProjection : Projection<PortabilityDetail>
{
    public PortabilityDetailProjection()
    {
        On<NumberReserved>(e => e.Offer.SubscriptionId, (_, e) => new(e.Offer.SubscriptionId, e.Offer.Msisdn, e.Offer.Epoch, PortPhase.Reserved, default, default, false));
        On<PortCancellationRequested>(e => e.Offer.SubscriptionId, (v, e) => new(e.Offer.SubscriptionId, e.Offer.Msisdn, e.Offer.Epoch,
            PortPhase.CancellationRequested, v?.WindowStart ?? default, v?.WindowEnd ?? default, true));
        On<NumberReservationReleased>(e => e.Offer.SubscriptionId, (v, e) => !e.Released ? v : new(e.Offer.SubscriptionId, e.Offer.Msisdn, e.Offer.Epoch, PortPhase.Cancelled, default, default, false));
        On<NumberChanged>(e => e.Offer.SubscriptionId, (v, e) => new(e.Offer.SubscriptionId, e.Offer.Msisdn, e.Offer.Epoch,
            e.Phase, e.WindowStart, e.WindowEnd, v?.CancellationRequested ?? false));
    }
}
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
public sealed record IncidentItem(string SubscriptionId, string Kind, int Attempt, bool Open);
public sealed class IncidentProjection : Projection<IncidentItem>
{
    public IncidentProjection()
    {
        On<IncidentOpened>(e => Incident.Key(e.SubscriptionId, e.Kind, e.Attempt), (_, e) => new(e.SubscriptionId, e.Kind, e.Attempt, true));
        On<IncidentResolved>(e => Incident.Key(e.SubscriptionId, e.Kind, e.Attempt), (_, e) => new(e.SubscriptionId, e.Kind, e.Attempt, false));
    }
}
