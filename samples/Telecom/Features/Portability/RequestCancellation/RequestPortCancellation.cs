using Wyrd;

namespace Telecom;

public sealed record RequestPortCancellation(string SubscriptionId, string Msisdn, long Epoch) : ICommand;

public sealed record PortCancellationRequested(Offer Offer) : IDomainEvent;


public sealed class RequestPortCancellationSlice : Slice<RequestPortCancellation, NumberPort, PortCancellationRequested, RuleError>
{
    public override object KeyOf(RequestPortCancellation command) => command.Msisdn;

    public override Result<PortCancellationRequested, RuleError> Decide(RequestPortCancellation command, NumberPort state)
    {
        var error = Validate(command, state);
        if (error is not null)
        {
            return error.Value;
        }

        return new PortCancellationRequested(state.Owner!);
    }

    private static RuleError? Validate(RequestPortCancellation command, NumberPort state)
    {
        if (!state.Owns(command.SubscriptionId, command.Epoch))
        {
            return RuleError.WrongOwner;
        }

        if (state.Phase == PortPhase.Completed)
        {
            return RuleError.Irreversible;
        }

        if (state.Phase is PortPhase.Reserved or PortPhase.Admitted)
        {
            return RuleError.NotReady;
        }

        if (state.Phase == PortPhase.Cancelled)
        {
            return RuleError.IllegalTransition;
        }

        return null;
    }
}
