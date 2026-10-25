using Wyrd;

namespace Telecom;

public sealed record MarkPortSubmitted(Offer Offer) : ICommand;

public sealed record NumberChanged(Offer Offer, PortPhase Phase, DateTimeOffset WindowStart, DateTimeOffset WindowEnd) : IDomainEvent;

public sealed class MarkPortSubmittedSlice : Slice<MarkPortSubmitted, NumberPort, NumberChanged, RuleError>
{
    public override object KeyOf(MarkPortSubmitted command) => command.Offer.Msisdn;

    public override Result<NumberChanged, RuleError> Decide(MarkPortSubmitted command, NumberPort state)
    {
        var error = Validate(command, state);
        if (error is not null)
        {
            return error.Value;
        }

        var phase = state.Phase;
        if (phase == PortPhase.Admitted)
        {
            phase = PortPhase.Submitted;
        }

        return new NumberChanged(state.Owner!, phase, state.WindowStart, state.WindowEnd);
    }

    private static RuleError? Validate(MarkPortSubmitted command, NumberPort state)
    {
        if (!state.Owns(command.Offer.SubscriptionId, command.Offer.Epoch))
        {
            return RuleError.WrongOwner;
        }

        return null;
    }
}
