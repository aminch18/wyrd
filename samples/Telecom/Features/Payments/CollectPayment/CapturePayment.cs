using Wyrd;

namespace Telecom;

public sealed record CapturePayment(Offer Offer) : ICommand;

public sealed record CaptureReceived(string SubscriptionId, string Receipt) : IDomainEvent;

public sealed class CapturePaymentIntegration(IProviderApi provider) : Integration<CapturePayment, CaptureReceived, ProviderError>
{
    public override object KeyOf(CapturePayment command) => command.Offer.SubscriptionId;
    public override async Task<Result<CaptureReceived, ProviderError>> ExecuteAsync(CapturePayment command, IntegrationContext context, CancellationToken cancellationToken)
    {
        var reply = await provider.CallAsync(PaymentRequest(command.Offer), context.IdempotencyKey, cancellationToken).ConfigureAwait(false);
        if (reply.Error is not null)
        {
            return reply.Error.Value;
        }

        return new CaptureReceived(command.Offer.SubscriptionId, reply.Receipt!);
    }
    public static ProviderRequest PaymentRequest(Offer offer) => new(ProviderAction.Capture, $"capture/{offer.SubscriptionId}", offer.SubscriptionId, offer.CustomerId, offer.InitialAmount, 0);
}
