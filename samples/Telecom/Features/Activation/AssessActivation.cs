using Wyrd.Processes;
using Wyrd;

namespace Telecom;

public sealed record AssessActivation(string SubscriptionId) : ICommand;

public sealed record ServiceOperational(string SubscriptionId, string ProfileId, int Attempt) : IDomainEvent;


public sealed class AssessActivationSlice : Slice<AssessActivation, Activation, ServiceOperational, RuleError>
{
    public override object KeyOf(AssessActivation command) => command.SubscriptionId;

    public override Result<ServiceOperational, RuleError> Decide(AssessActivation command, Activation state)
    {
        var error = Validate(state);
        if (error is not null)
        {
            return error.Value;
        }

        return new ServiceOperational(command.SubscriptionId, state.ProfileId!, state.Attempt);
    }

    private static RuleError? Validate(Activation state)
    {
        if (state.Operational)
        {
            return RuleError.AlreadyExists;
        }

        if (!state.Ready)
        {
            return RuleError.NotReady;
        }

        return null;
    }
}

public static partial class Flows
{
    public static Process ActivationAssessment => Process.StartOn<ActivationObserved>(e => e.EvidenceKey)
        .Then(Assess).Retry(3.Times);

    private static AssessActivation Assess(ActivationObserved observed) => new(observed.SubscriptionId);
}
