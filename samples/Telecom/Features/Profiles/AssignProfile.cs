using Wyrd.Processes;
using Wyrd;

namespace Telecom;

public sealed record AssignProfile(string SubscriptionId, string CustomerId, string ProfileId, int Attempt) : ICommand;

public sealed record ProfileAssigned(string SubscriptionId, string CustomerId, string ProfileId, int Attempt) : IDomainEvent;

public sealed class AssignProfileSlice : Slice<AssignProfile, Activation, ProfileAssigned, RuleError>
{
    public override object KeyOf(AssignProfile command) => command.SubscriptionId;

    public override Result<ProfileAssigned, RuleError> Decide(AssignProfile command, Activation state)
    {
        var error = Validate(command, state);
        if (error is not null)
        {
            return error.Value;
        }

        return new ProfileAssigned(command.SubscriptionId, command.CustomerId, command.ProfileId, command.Attempt);
    }

    private static RuleError? Validate(AssignProfile command, Activation state)
    {
        if (!HasValidIdentity(command))
        {
            return RuleError.InvalidInput;
        }

        if (state.Operational)
        {
            return RuleError.Irreversible;
        }

        if (command.Attempt <= state.Attempt || command.Attempt != state.Attempt + 1)
        {
            return RuleError.StaleAttempt;
        }

        if (state.ProfileId == command.ProfileId)
        {
            return RuleError.IllegalTransition;
        }

        if (state.CustomerId is not null && state.CustomerId != command.CustomerId)
        {
            return RuleError.WrongOwner;
        }

        return null;
    }

    private static bool HasValidIdentity(AssignProfile command)
    {
        return Offer.ValidId(command.SubscriptionId)
            && Offer.ValidId(command.ProfileId)
            && Offer.ValidId(command.CustomerId);
    }
}

public static partial class Flows
{
    public static Process InitialProfile => Process.StartOn<PaymentCaptured>(e => e.Offer.SubscriptionId)
        .Then(Assign).Retry(3.Times);

    private static AssignProfile Assign(PaymentCaptured captured) => new(captured.Offer.SubscriptionId, captured.Offer.CustomerId, captured.Offer.ProfileId, 1);
}
