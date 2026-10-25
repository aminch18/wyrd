using Wyrd;

namespace Telecom;

public enum ActivationFact
{
    Payment,
    PortCompleted,
    Provisioned,
    Connectivity,
}

public sealed record ObserveActivation(
    string SubscriptionId,
    ActivationFact Fact,
    string ProfileId = "",
    int Attempt = 0) : ICommand;

public sealed record ActivationObserved(string SubscriptionId, string EvidenceKey, Activation Next) : IDomainEvent;

public sealed class ObserveActivationSlice : Slice<ObserveActivation, Activation, ActivationObserved, RuleError>
{
    public override object KeyOf(ObserveActivation command) => command.SubscriptionId;

    public override Result<ActivationObserved, RuleError> Decide(ObserveActivation command, Activation state)
    {
        var error = Validate(command, state);
        if (error is not null)
        {
            return error.Value;
        }

        var next = ApplyFact(command.Fact, state);
        var evidenceKey = $"{command.SubscriptionId}/{command.Fact}/{command.Attempt}";
        return new ActivationObserved(command.SubscriptionId, evidenceKey, next);
    }

    private static RuleError? Validate(ObserveActivation command, Activation state)
    {
        if (command.Fact is ActivationFact.Payment or ActivationFact.PortCompleted)
        {
            return null;
        }

        if (command.Fact is ActivationFact.Provisioned or ActivationFact.Connectivity)
        {
            if (command.Attempt != state.Attempt || command.ProfileId != state.ProfileId)
            {
                return RuleError.StaleAttempt;
            }
        }

        return null;
    }

    private static Activation ApplyFact(ActivationFact fact, Activation state)
    {
        return fact switch
        {
            ActivationFact.Payment => state with { Paid = true },
            ActivationFact.PortCompleted => state with { Ported = true },
            ActivationFact.Provisioned => state with { Provisioned = true },
            ActivationFact.Connectivity => state with { Connected = true },
            _ => state,
        };
    }
}
