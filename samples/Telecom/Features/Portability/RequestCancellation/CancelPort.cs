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
