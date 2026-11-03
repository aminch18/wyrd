using Wyrd;

namespace Telecom;

public sealed record ReleaseUnsubmittedNumber(Offer Offer) : ICommand;

public sealed record NumberReservationReleased(Offer Offer, bool Released) : IDomainEvent;

public sealed class ReleaseUnsubmittedNumberSlice : Slice<ReleaseUnsubmittedNumber, NumberPort, NumberReservationReleased, RuleError>
{
    public override object KeyOf(ReleaseUnsubmittedNumber command) => command.Offer.Msisdn;

    public override Result<NumberReservationReleased, RuleError> Decide(ReleaseUnsubmittedNumber command, NumberPort state)
    {
        var error = Validate(command, state);
        if (error is not null)
        {
            return error.Value;
        }

        // A different owner means this request has no reservation to release.
        var released = state.Owns(command.Offer.SubscriptionId, command.Offer.Epoch);
        return new NumberReservationReleased(command.Offer, released);
    }

    private static RuleError? Validate(ReleaseUnsubmittedNumber command, NumberPort state)
    {
        if (!state.Owns(command.Offer.SubscriptionId, command.Offer.Epoch))
        {
            return null;
        }

        // Admission may already have queued external work. Never free that reservation locally.
        if (state.Phase is not (PortPhase.Reserved or PortPhase.Cancelled))
        {
            return RuleError.Irreversible;
        }

        return null;
    }
}
