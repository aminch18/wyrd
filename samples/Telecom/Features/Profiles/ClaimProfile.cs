using Wyrd;

namespace Telecom;

public sealed record ClaimProfile(string SubscriptionId, string CustomerId, string ProfileId, int Attempt) : ICommand;

public sealed record ProfileClaimed(string SubscriptionId, string CustomerId, string ProfileId, int Attempt) : IDomainEvent;

public sealed class ClaimProfileSlice : Slice<ClaimProfile, ProfileLease, ProfileClaimed, RuleError>
{
    public override object KeyOf(ClaimProfile command) => command.ProfileId;

    public override Result<ProfileClaimed, RuleError> Decide(ClaimProfile command, ProfileLease state)
    {
        var error = Validate(command, state);
        if (error is not null)
        {
            return error.Value;
        }

        return new ProfileClaimed(command.SubscriptionId, command.CustomerId, command.ProfileId, command.Attempt);
    }

    private static RuleError? Validate(ClaimProfile command, ProfileLease state)
    {
        if (!HasValidIdentity(command))
        {
            return RuleError.InvalidInput;
        }

        if (IsOwnedByAnotherAttempt(command, state))
        {
            return RuleError.AlreadyExists;
        }

        return null;
    }

    private static bool HasValidIdentity(ClaimProfile command)
    {
        return Offer.ValidId(command.SubscriptionId)
            && Offer.ValidId(command.ProfileId)
            && Offer.ValidId(command.CustomerId)
            && command.Attempt >= 1;
    }

    private static bool IsOwnedByAnotherAttempt(ClaimProfile command, ProfileLease state)
    {
        if (state.SubscriptionId is null)
        {
            return false;
        }

        return state.SubscriptionId != command.SubscriptionId
            || state.CustomerId != command.CustomerId
            || state.Attempt != command.Attempt;
    }
}
