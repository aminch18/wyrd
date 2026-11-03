using Wyrd.Processes;
using Wyrd;

namespace Telecom;

public sealed record ReconcilePayment(Offer Offer, int Review) : ICommand;

public sealed record PaymentReconciliationRequested(Offer Offer, int Review) : IDomainEvent;

public sealed class ReconcilePaymentSlice : Slice<ReconcilePayment, Payment, PaymentReconciliationRequested, RuleError>
{
    public override object KeyOf(ReconcilePayment command) => command.Offer.SubscriptionId;

    public override Result<PaymentReconciliationRequested, RuleError> Decide(ReconcilePayment command, Payment state)
    {
        var error = Validate(command, state);
        if (error is not null)
        {
            return error.Value;
        }

        return new PaymentReconciliationRequested(command.Offer, command.Review);
    }

    private static RuleError? Validate(ReconcilePayment command, Payment state)
    {
        if (state.Offer != command.Offer)
        {
            return RuleError.WrongOwner;
        }

        if (state.Review == int.MaxValue || command.Review != state.Review + 1)
        {
            return RuleError.StaleAttempt;
        }

        return null;
    }
}

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

        return new CaptureReceived(command.Offer.SubscriptionId, reply.RequireReceipt());
    }
}

public static partial class Flows
{
    public static Process PaymentReconciliation => Process.StartOn<PaymentReconciliationRequested>(e => $"{e.Offer.SubscriptionId}/{e.Review}")
        .Then(Find).Retry(3.Times).NoCompensation("Read-only provider inquiry")
        .Then(StoreReconciledCapture).Retry(3.Times)
        .OnCompensated(ReconciliationIncident).Retry(3.Times);

    private static FindPayment Find(PaymentReconciliationRequested requested) => new(requested.Offer);

    private static RecordCapture StoreReconciledCapture(PaymentReconciliationRequested requested, CaptureReceived receipt) => new(requested.Offer, receipt.Receipt);

    private static OpenIncident ReconciliationIncident(PaymentReconciliationRequested requested) => new(requested.Offer.SubscriptionId, "reconciliation", requested.Review);
}
