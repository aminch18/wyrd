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
        var error = state.CheckProfileAssignment(command);
        if (error is not null)
        {
            return error.Value;
        }

        return new ProfileAssigned(command.SubscriptionId, command.CustomerId, command.ProfileId, command.Attempt);
    }
}

public static partial class Flows
{
    public static Process InitialProfile => Process.StartOn<PaymentCaptured>(e => e.Offer.SubscriptionId)
        .Then(Assign).Retry(3.Times);

    private static AssignProfile Assign(PaymentCaptured captured) => new(captured.Offer.SubscriptionId, captured.Offer.CustomerId, captured.Offer.ProfileId, 1);
}
