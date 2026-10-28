using Wyrd.Processes;
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

public static partial class Flows
{
    public static Process PortConfirmation => Process.StartOn<PortConfirmedV1>(e => PortKey(e.SubscriptionId, e.Msisdn, e.Epoch))
        .Then(ConfirmPort).Retry(3.Times);

    public static Process PortCompletion => Process.StartOn<PortCompletedV1>(e => PortKey(e.SubscriptionId, e.Msisdn, e.Epoch))
        .Then(CompletePort).Retry(3.Times);

    public static Process CancellationConfirmation => Process.StartOn<PortCancelledV1>(e => PortKey(e.SubscriptionId, e.Msisdn, e.Epoch))
        .Then(ConfirmCancellation).Retry(3.Times);

    private static ApplyPortNotice ConfirmPort(PortConfirmedV1 confirmed) => new(confirmed.SubscriptionId, confirmed.Msisdn, confirmed.Epoch, PortFact.Confirmed, confirmed.WindowStart, confirmed.WindowEnd);

    private static ApplyPortNotice CompletePort(PortCompletedV1 completed) => new(completed.SubscriptionId, completed.Msisdn, completed.Epoch, PortFact.Completed, default, default);

    private static ApplyPortNotice ConfirmCancellation(PortCancelledV1 cancelled) => new(cancelled.SubscriptionId, cancelled.Msisdn, cancelled.Epoch, PortFact.Cancelled, default, default);
}
