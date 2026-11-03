using Wyrd;

namespace Telecom;

public sealed record AdmitSubscription(Offer Offer) : ICommand;

public sealed record SubscriptionAdmitted(Offer Offer) : IDomainEvent;

public sealed class AdmitSubscriptionSlice : Slice<AdmitSubscription, NumberPort, SubscriptionAdmitted, RuleError>
{
    public override object KeyOf(AdmitSubscription command) => command.Offer.Msisdn;

    public override Result<SubscriptionAdmitted, RuleError> Decide(AdmitSubscription command, NumberPort state)
    {
        var error = Validate(command, state);
        if (error is not null)
        {
            return error.Value;
        }

        return new SubscriptionAdmitted(command.Offer);
    }

    private static RuleError? Validate(AdmitSubscription command, NumberPort state)
    {
        if (state.Owner != command.Offer)
        {
            return RuleError.WrongOwner;
        }

        if (state.Phase != PortPhase.Reserved)
        {
            return RuleError.IllegalTransition;
        }

        if (command.Offer.InitialAmount > AdmissionLimit)
        {
            return RuleError.InvalidInput;
        }

        return null;
    }

    // Sample credit policy. Admission seals the reservation before payment work is queued.
    private const decimal AdmissionLimit = 250m;
}
