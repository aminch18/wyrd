using Wyrd;

namespace Telecom;

public enum PortFact
{
    Confirmed,
    Completed,
    Cancelled,
}

public sealed record ApplyPortNotice(
    string SubscriptionId,
    string Msisdn,
    long Epoch,
    PortFact Fact,
    DateTimeOffset WindowStart,
    DateTimeOffset WindowEnd) : ICommand;

public sealed class ApplyPortNoticeSlice : Slice<ApplyPortNotice, NumberPort, NumberChanged, RuleError>
{
    public override object KeyOf(ApplyPortNotice command) => command.Msisdn;

    public override Result<NumberChanged, RuleError> Decide(ApplyPortNotice command, NumberPort state)
    {
        var error = Validate(command, state);
        if (error is not null)
        {
            return error.Value;
        }

        var phase = NextPhase(command.Fact, state.Phase);
        var windowStart = state.WindowStart;
        var windowEnd = state.WindowEnd;

        if (command.Fact == PortFact.Confirmed)
        {
            windowStart = command.WindowStart;
            windowEnd = command.WindowEnd;
        }

        return new NumberChanged(state.Owner!, phase, windowStart, windowEnd);
    }

    private static RuleError? Validate(ApplyPortNotice command, NumberPort state)
    {
        if (command.Fact is not (PortFact.Confirmed or PortFact.Completed or PortFact.Cancelled))
        {
            return RuleError.InvalidInput;
        }

        if (!state.Owns(command.SubscriptionId, command.Epoch))
        {
            return RuleError.WrongOwner;
        }

        if (state.Phase == PortPhase.Cancelled)
        {
            return RuleError.StaleAttempt;
        }

        if (command.Fact == PortFact.Confirmed && command.WindowEnd <= command.WindowStart)
        {
            return RuleError.InvalidInput;
        }

        if (command.Fact == PortFact.Cancelled && state.Phase != PortPhase.CancellationRequested)
        {
            return RuleError.IllegalTransition;
        }

        return null;
    }

    private static PortPhase NextPhase(PortFact fact, PortPhase currentPhase)
    {
        // Completion is irreversible, even when an earlier confirmation arrives late.
        if (currentPhase == PortPhase.Completed)
        {
            return PortPhase.Completed;
        }

        // Confirmation cannot withdraw a cancellation request.
        if (fact == PortFact.Confirmed && currentPhase == PortPhase.CancellationRequested)
        {
            return PortPhase.CancellationRequested;
        }

        return fact switch
        {
            PortFact.Completed => PortPhase.Completed,
            PortFact.Cancelled => PortPhase.Cancelled,
            _ => PortPhase.Confirmed,
        };
    }
}
