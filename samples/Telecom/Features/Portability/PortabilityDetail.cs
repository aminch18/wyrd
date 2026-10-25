using Wyrd;

namespace Telecom;

public sealed record PortabilityDetail(
    string SubscriptionId,
    string Msisdn,
    long Epoch,
    PortPhase Phase,
    DateTimeOffset WindowStart,
    DateTimeOffset WindowEnd,
    bool CancellationRequested);

public sealed class PortabilityDetailProjection : Projection<PortabilityDetail>
{
    public PortabilityDetailProjection()
    {
        On<NumberReserved>(e => e.Offer.SubscriptionId, ApplyReserved);
        On<PortCancellationRequested>(e => e.Offer.SubscriptionId, ApplyCancellationRequested);
        On<NumberReservationReleased>(e => e.Offer.SubscriptionId, ApplyReleased);
        On<NumberChanged>(e => e.Offer.SubscriptionId, ApplyChanged);
    }

    private static PortabilityDetail ApplyReserved(PortabilityDetail? view, NumberReserved reserved)
    {
        var offer = reserved.Offer;
        return new PortabilityDetail(offer.SubscriptionId, offer.Msisdn, offer.Epoch, PortPhase.Reserved, default, default, false);
    }

    private static PortabilityDetail ApplyCancellationRequested(PortabilityDetail? view, PortCancellationRequested requested)
    {
        var offer = requested.Offer;
        var windowStart = view?.WindowStart ?? default;
        var windowEnd = view?.WindowEnd ?? default;
        return new PortabilityDetail(offer.SubscriptionId, offer.Msisdn, offer.Epoch, PortPhase.CancellationRequested, windowStart, windowEnd, true);
    }

    private static PortabilityDetail? ApplyReleased(PortabilityDetail? view, NumberReservationReleased released)
    {
        if (!released.Released)
        {
            return view;
        }

        var offer = released.Offer;
        return new PortabilityDetail(offer.SubscriptionId, offer.Msisdn, offer.Epoch, PortPhase.Cancelled, default, default, false);
    }

    private static PortabilityDetail ApplyChanged(PortabilityDetail? view, NumberChanged changed)
    {
        var offer = changed.Offer;
        var cancellationRequested = view?.CancellationRequested ?? false;
        return new PortabilityDetail(offer.SubscriptionId, offer.Msisdn, offer.Epoch, changed.Phase, changed.WindowStart, changed.WindowEnd, cancellationRequested);
    }
}
