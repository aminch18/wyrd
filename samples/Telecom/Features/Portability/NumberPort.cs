using Wyrd;

namespace Telecom;

public enum PortPhase
{
    Vacant,
    Reserved,
    Admitted,
    Submitted,
    Confirmed,
    CancellationRequested,
    Cancelled,
    Completed,
}

// One bounded snapshot per phone number. Epochs fence requests even after inbox retention expires.
public sealed record NumberPort(Offer? Owner, PortPhase Phase, long Epoch, DateTimeOffset WindowStart, DateTimeOffset WindowEnd) : IState<NumberPort>
{
    public static NumberPort Initial { get; } = new(null, PortPhase.Vacant, 0, default, default);
    public bool Owns(string id, long epoch) => Owner?.SubscriptionId == id && Epoch == epoch;
    public NumberPort Evolve(IDomainEvent domainEvent) => domainEvent switch
    {
        NumberReserved e => new(e.Offer, PortPhase.Reserved, e.Offer.Epoch, default, default),
        SubscriptionAdmitted => this with { Phase = PortPhase.Admitted },
        NumberReservationReleased { Released: true } => this with { Phase = PortPhase.Cancelled },
        NumberChanged e => this with { Phase = e.Phase, WindowStart = e.WindowStart, WindowEnd = e.WindowEnd },
        PortCancellationRequested => this with { Phase = PortPhase.CancellationRequested },
        _ => this,
    };
}
