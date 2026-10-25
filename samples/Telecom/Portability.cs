using Wyrd;

namespace Telecom;

public enum PortPhase { Vacant, Reserved, Admitted, Submitted, Confirmed, CancellationRequested, Cancelled, Completed }
public enum PortFact { Confirmed, Completed, Cancelled }
public sealed record ClaimNumber(Offer Offer) : ICommand;
public sealed record NumberReserved(Offer Offer) : IDomainEvent;
public sealed record MarkPortSubmitted(Offer Offer) : ICommand;
public sealed record ApplyPortNotice(string SubscriptionId, string Msisdn, long Epoch, PortFact Fact,
    DateTimeOffset WindowStart, DateTimeOffset WindowEnd) : ICommand;
public sealed record RequestPortCancellation(string SubscriptionId, string Msisdn, long Epoch) : ICommand;
public sealed record NumberChanged(Offer Offer, PortPhase Phase, DateTimeOffset WindowStart, DateTimeOffset WindowEnd) : IDomainEvent;
public sealed record PortCancellationRequested(Offer Offer) : IDomainEvent;

// One bounded snapshot per phone number. Epochs fence requests even after inbox retention expires.
public sealed record NumberPort(Offer? Owner, PortPhase Phase, long Epoch, DateTimeOffset WindowStart, DateTimeOffset WindowEnd) : IState<NumberPort>
{
    public static NumberPort Initial { get; } = new(null, PortPhase.Vacant, 0, default, default);
    public bool Owns(string id, long epoch) => Owner?.SubscriptionId == id && Epoch == epoch;
    public NumberPort Evolve(IDomainEvent domainEvent) => domainEvent switch
    {
        NumberReserved e => new(e.Offer, PortPhase.Reserved, e.Offer.Epoch, default, default),
        SubscriptionAdmitted => this with { Phase = PortPhase.Admitted },
        NumberReservationReleased { Released: true } => this with { Phase = PortPhase.Cancelled },
        NumberChanged e => this with { Phase = e.Phase, WindowStart = e.WindowStart, WindowEnd = e.WindowEnd },
        PortCancellationRequested => this with { Phase = PortPhase.CancellationRequested },
        _ => this,
    };
}

public sealed class ClaimNumberSlice : Slice<ClaimNumber, NumberPort, NumberReserved, RuleError>
{
    public override object KeyOf(ClaimNumber command) => command.Offer.Msisdn;
    public override Result<NumberReserved, RuleError> Decide(ClaimNumber command, NumberPort state) =>
        !command.Offer.IsValid ? RuleError.InvalidInput : command.Offer.Epoch <= state.Epoch ? RuleError.StaleAttempt
        : state.Phase is PortPhase.Vacant or PortPhase.Cancelled or PortPhase.Completed
            ? new NumberReserved(command.Offer) : RuleError.AlreadyExists;
}

public sealed class MarkPortSubmittedSlice : Slice<MarkPortSubmitted, NumberPort, NumberChanged, RuleError>
{
    public override object KeyOf(MarkPortSubmitted command) => command.Offer.Msisdn;
    public override Result<NumberChanged, RuleError> Decide(MarkPortSubmitted command, NumberPort state) =>
        !state.Owns(command.Offer.SubscriptionId, command.Offer.Epoch) ? RuleError.WrongOwner
        : new NumberChanged(state.Owner!, state.Phase == PortPhase.Admitted ? PortPhase.Submitted : state.Phase, state.WindowStart, state.WindowEnd);
}

public sealed class ApplyPortNoticeSlice : Slice<ApplyPortNotice, NumberPort, NumberChanged, RuleError>
{
    public override object KeyOf(ApplyPortNotice command) => command.Msisdn;
    public override Result<NumberChanged, RuleError> Decide(ApplyPortNotice command, NumberPort state)
    {
        if (command.Fact is not (PortFact.Confirmed or PortFact.Completed or PortFact.Cancelled)) return RuleError.InvalidInput;
        if (!state.Owns(command.SubscriptionId, command.Epoch)) return RuleError.WrongOwner;
        if (state.Phase == PortPhase.Cancelled) return RuleError.StaleAttempt;
        if (command.Fact == PortFact.Confirmed && command.WindowEnd <= command.WindowStart) return RuleError.InvalidInput;
        if (command.Fact == PortFact.Cancelled && state.Phase != PortPhase.CancellationRequested) return RuleError.IllegalTransition;
        // Completion can overtake a cancellation request. Confirmation cannot regress either one.
        var next = state.Phase == PortPhase.Completed ? PortPhase.Completed : command.Fact switch
        {
            PortFact.Completed => PortPhase.Completed,
            PortFact.Cancelled => PortPhase.Cancelled,
            PortFact.Confirmed when state.Phase == PortPhase.CancellationRequested => PortPhase.CancellationRequested,
            _ => PortPhase.Confirmed,
        };
        return new NumberChanged(state.Owner!, next,
            command.Fact == PortFact.Confirmed ? command.WindowStart : state.WindowStart,
            command.Fact == PortFact.Confirmed ? command.WindowEnd : state.WindowEnd);
    }
}

public sealed class RequestPortCancellationSlice : Slice<RequestPortCancellation, NumberPort, PortCancellationRequested, RuleError>
{
    public override object KeyOf(RequestPortCancellation command) => command.Msisdn;
    public override Result<PortCancellationRequested, RuleError> Decide(RequestPortCancellation command, NumberPort state) =>
        !state.Owns(command.SubscriptionId, command.Epoch) ? RuleError.WrongOwner
        : state.Phase == PortPhase.Completed ? RuleError.Irreversible
        : state.Phase is PortPhase.Reserved or PortPhase.Admitted ? RuleError.NotReady
        : state.Phase == PortPhase.Cancelled ? RuleError.IllegalTransition : new PortCancellationRequested(state.Owner!);
}

public sealed record ReleaseUnsubmittedNumber(Offer Offer) : ICommand;
public sealed record NumberReservationReleased(Offer Offer, bool Released) : IDomainEvent;
public sealed class ReleaseUnsubmittedNumberSlice : Slice<ReleaseUnsubmittedNumber, NumberPort, NumberReservationReleased, RuleError>
{
    public override object KeyOf(ReleaseUnsubmittedNumber command) => command.Offer.Msisdn;
    public override Result<NumberReservationReleased, RuleError> Decide(ReleaseUnsubmittedNumber command, NumberPort state) =>
        !state.Owns(command.Offer.SubscriptionId, command.Offer.Epoch) ? new NumberReservationReleased(command.Offer, false)
        : state.Phase is PortPhase.Reserved or PortPhase.Cancelled ? new NumberReservationReleased(command.Offer, true)
        : RuleError.Irreversible;
}
