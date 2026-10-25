using Wyrd.Processes;
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

public static partial class Flows
{
    public static Process PortSubmission => Process.StartOn<PaymentCaptured>(e => PortKey(e.Offer.SubscriptionId, e.Offer.Msisdn, e.Offer.Epoch))
        .Then(Submit).Retry(3.Times).NoCompensation("Request cancellation is a separate protocol; completion cannot be reversed")
        .Then(StoreSubmission).Retry(3.Times).NoCompensation("Submission is a fact")
        .Await<PortCompletedV1>(e => PortKey(e.SubscriptionId, e.Msisdn, e.Epoch)).Within(2.Days)
        .OnCompensated(PortIncident).Retry(3.Times);

    private static SubmitPort Submit(PaymentCaptured captured) => new(captured.Offer);

    private static MarkPortSubmitted StoreSubmission(PaymentCaptured captured) => new(captured.Offer);

    private static OpenIncident PortIncident(PaymentCaptured captured) => new(captured.Offer.SubscriptionId, "port-deadline", 0);
}
