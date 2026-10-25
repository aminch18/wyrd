using Wyrd.Processes;
using Wyrd;

namespace Telecom;

public sealed record CancelPort(Offer Offer) : ICommand;

public sealed record PortCancellationReceived(string SubscriptionId, string Receipt) : IDomainEvent;

public sealed class CancelPortIntegration(IProviderApi provider) : Integration<CancelPort, PortCancellationReceived, ProviderError>
{
    public override async Task<Result<PortCancellationReceived, ProviderError>> ExecuteAsync(CancelPort command, IntegrationContext context, CancellationToken cancellationToken)
    {
        var offer = command.Offer;
        var reply = await provider.CallAsync(new(ProviderAction.CancelPort, $"cancel/{offer.Msisdn}/{offer.Epoch}", offer.Msisdn, offer.SubscriptionId, 0, 0), context.IdempotencyKey, cancellationToken).ConfigureAwait(false);
        if (reply.Error is not null)
        {
            return reply.Error.Value;
        }

        return new PortCancellationReceived(offer.SubscriptionId, reply.Receipt!);
    }
}

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

public static partial class Flows
{
    public static Process PortCancellation => Process.StartOn<PortCancellationRequested>(e => $"{e.Offer.SubscriptionId}/{e.Offer.Epoch}")
        .Then(CancelPort).Retry(3.Times);

    private static CancelPort CancelPort(PortCancellationRequested requested) => new(requested.Offer);
}
