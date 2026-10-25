using Wyrd;

namespace Telecom;

public sealed record SubmitPort(Offer Offer) : ICommand;

public sealed record PortSubmissionReceived(string SubscriptionId, string Receipt) : IDomainEvent;

public sealed class SubmitPortIntegration(IProviderApi provider) : Integration<SubmitPort, PortSubmissionReceived, ProviderError>
{
    public override async Task<Result<PortSubmissionReceived, ProviderError>> ExecuteAsync(SubmitPort command, IntegrationContext context, CancellationToken cancellationToken)
    {
        var offer = command.Offer;
        var reply = await provider.CallAsync(new(ProviderAction.SubmitPort, $"port/{offer.Msisdn}/{offer.Epoch}", offer.Msisdn, offer.SubscriptionId, 0, 0), context.IdempotencyKey, cancellationToken).ConfigureAwait(false);
        if (reply.Error is not null)
        {
            return reply.Error.Value;
        }

        return new PortSubmissionReceived(offer.SubscriptionId, reply.Receipt!);
    }
}
