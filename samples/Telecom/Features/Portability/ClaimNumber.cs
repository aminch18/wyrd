using Wyrd;

namespace Telecom;

public sealed record ClaimNumber(Offer Offer) : ICommand;

public sealed record NumberReserved(Offer Offer) : IDomainEvent;

public sealed class ClaimNumberSlice : Slice<ClaimNumber, NumberPort, NumberReserved, RuleError>
{
    public override object KeyOf(ClaimNumber command) => command.Offer.Msisdn;

    public override Result<NumberReserved, RuleError> Decide(ClaimNumber command, NumberPort state)
    {
        var error = Validate(command, state);
        if (error is not null)
        {
            return error.Value;
        }

        return new NumberReserved(command.Offer);
    }

    private static RuleError? Validate(ClaimNumber command, NumberPort state)
    {
        if (!command.Offer.IsValid)
        {
            return RuleError.InvalidInput;
        }

        if (command.Offer.Epoch <= state.Epoch)
        {
            return RuleError.StaleAttempt;
        }

        if (state.Phase is not (PortPhase.Vacant or PortPhase.Cancelled or PortPhase.Completed))
        {
            return RuleError.AlreadyExists;
        }

        return null;
    }
}
