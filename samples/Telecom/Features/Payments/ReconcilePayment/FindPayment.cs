using Wyrd;

namespace Telecom;

public sealed record FindPayment(Offer Offer) : ICommand;

public sealed class FindPaymentIntegration(IProviderApi provider) : Integration<FindPayment, CaptureReceived, ProviderError>
{
    public override async Task<Result<CaptureReceived, ProviderError>> ExecuteAsync(FindPayment command, IntegrationContext context, CancellationToken cancellationToken)
    {
        var reply = await provider.CallAsync(CapturePaymentIntegration.PaymentRequest(command.Offer) with { Action = ProviderAction.FindCapture }, context.IdempotencyKey, cancellationToken).ConfigureAwait(false);
        if (reply.Error is not null)
        {
            return reply.Error.Value;
        }

        return new CaptureReceived(command.Offer.SubscriptionId, reply.Receipt!);
    }
}
