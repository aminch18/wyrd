using Wyrd.Processes;
using Wyrd;

namespace Telecom;

// A linear process cannot filter a trigger. The domain command rejects non-completion facts;
// its one-step process compensates with no undo. This extra plumbing is documented as a DSL cost.
public sealed record RecordCompletedPort(string SubscriptionId, PortPhase Phase) : ICommand;
public sealed class RecordCompletedPortSlice : Slice<RecordCompletedPort, Activation, ActivationObserved, RuleError>
{
    public override object KeyOf(RecordCompletedPort command) => command.SubscriptionId;

    public override Result<ActivationObserved, RuleError> Decide(RecordCompletedPort command, Activation state)
    {
        var error = Validate(command);
        if (error is not null)
        {
            return error.Value;
        }

        return new ActivationObserved(command.SubscriptionId, $"{command.SubscriptionId}/port", state with { Ported = true });
    }

    private static RuleError? Validate(RecordCompletedPort command)
    {
        if (command.Phase != PortPhase.Completed)
        {
            return RuleError.NotReady;
        }

        return null;
    }
}

public static partial class Flows
{
    public static Process PortEvidence => Process.StartOn<NumberChanged>(e => $"{e.Offer.SubscriptionId}/{e.Phase}")
        .Then(ObservePort).Retry(3.Times);

    private static RecordCompletedPort ObservePort(NumberChanged changed) => new(changed.Offer.SubscriptionId, changed.Phase);
}
