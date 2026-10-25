using Wyrd;

namespace Telecom;

public sealed record CustomerProgress(
    string SubscriptionId,
    string? Msisdn = null,
    bool Paid = false,
    bool Confirmed = false,
    bool Ported = false,
    int Attempt = 0,
    string? ProfileId = null,
    bool Provisioned = false,
    bool Connected = false,
    bool Operational = false,
    bool CancellationRequested = false,
    bool Cancelled = false,
    bool Declined = false)
{
    public string Stage => DescribeStage();

    private string DescribeStage()
    {
        if (Operational)
        {
            return "Operational";
        }
        if (Ported && !Connected)
        {
            return "Ported: connectivity pending";
        }
        if (Ported)
        {
            return "Activation pending";
        }
        if (Cancelled)
        {
            return "Port cancelled";
        }
        if (CancellationRequested)
        {
            return "Cancellation requested";
        }
        if (Declined)
        {
            return "Subscription declined";
        }
        if (Confirmed)
        {
            return "Porting window agreed";
        }
        if (Paid)
        {
            return "Port requested";
        }
        return "Payment pending";
    }
}

public sealed class CustomerProgressProjection : Projection<CustomerProgress>
{
    public CustomerProgressProjection()
    {
        On<SubscriptionOpened>(e => e.Offer.SubscriptionId, ApplyOpened);
        On<SubscriptionDeclined>(e => e.SubscriptionId, ApplyDeclined);
        On<NumberReservationReleased>(e => e.Offer.SubscriptionId, ApplyReleased);
        On<PortCancellationRequested>(e => e.Offer.SubscriptionId, ApplyCancellationRequested);
        On<PaymentCaptured>(e => e.Offer.SubscriptionId, ApplyPaymentCaptured);
        On<NumberChanged>(e => e.Offer.SubscriptionId, ApplyNumberChanged);
        On<ProfileAssigned>(e => e.SubscriptionId, ApplyProfileAssigned);
        On<ActivationObserved>(e => e.SubscriptionId, ApplyActivationObserved);
        On<ServiceOperational>(e => e.SubscriptionId, ApplyOperational);
    }

    private static CustomerProgress ApplyOpened(CustomerProgress? view, SubscriptionOpened opened)
    {
        view ??= new CustomerProgress(opened.Offer.SubscriptionId);
        return view with { Msisdn = opened.Offer.Msisdn };
    }

    private static CustomerProgress ApplyDeclined(CustomerProgress? view, SubscriptionDeclined declined)
    {
        view ??= new CustomerProgress(declined.SubscriptionId);
        return view with { Declined = true };
    }

    private static CustomerProgress ApplyReleased(CustomerProgress? view, NumberReservationReleased released)
    {
        view ??= new CustomerProgress(released.Offer.SubscriptionId);
        return view with { Cancelled = view.Cancelled || released.Released };
    }

    private static CustomerProgress ApplyCancellationRequested(CustomerProgress? view, PortCancellationRequested requested)
    {
        view ??= new CustomerProgress(requested.Offer.SubscriptionId);
        return view with { CancellationRequested = true };
    }

    private static CustomerProgress ApplyPaymentCaptured(CustomerProgress? view, PaymentCaptured captured)
    {
        view ??= new CustomerProgress(captured.Offer.SubscriptionId);
        return view with { Paid = true };
    }

    private static CustomerProgress ApplyNumberChanged(CustomerProgress? view, NumberChanged changed)
    {
        view ??= new CustomerProgress(changed.Offer.SubscriptionId);
        return view with
        {
            Confirmed = view.Confirmed || changed.Phase == PortPhase.Confirmed,
            Ported = view.Ported || changed.Phase == PortPhase.Completed,
            Cancelled = view.Cancelled || changed.Phase == PortPhase.Cancelled,
        };
    }

    private static CustomerProgress? ApplyProfileAssigned(CustomerProgress? view, ProfileAssigned assigned)
    {
        if (assigned.Attempt <= (view?.Attempt ?? 0))
        {
            return view;
        }

        view ??= new CustomerProgress(assigned.SubscriptionId);
        return view with
        {
            Attempt = assigned.Attempt,
            ProfileId = assigned.ProfileId,
            Provisioned = false,
            Connected = false,
        };
    }

    private static CustomerProgress ApplyActivationObserved(CustomerProgress? view, ActivationObserved observed)
    {
        view ??= new CustomerProgress(observed.SubscriptionId);
        var facts = observed.Next;
        return view with
        {
            Paid = view.Paid || facts.Paid,
            Ported = view.Ported || facts.Ported,
            Attempt = facts.Attempt,
            ProfileId = facts.ProfileId,
            Provisioned = facts.Provisioned,
            Connected = facts.Connected,
        };
    }

    private static CustomerProgress ApplyOperational(CustomerProgress? view, ServiceOperational operational)
    {
        view ??= new CustomerProgress(operational.SubscriptionId);
        return view with { Operational = true };
    }
}
