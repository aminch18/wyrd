using Microsoft.AspNetCore.Mvc;
using MinApiLib.Endpoints;
using Telecom.Features.Subscriptions;
using Telecom.Shared;
using Wyrd.Runtime;

namespace Telecom.Features.Payments;

public sealed record ConfirmPaymentEndpoint() : Post("/webhooks/payments")
{
    public static async Task<IResult> HandleAsync(
        [FromBody] PaymentConfirmedV1 notice,
        [FromServices] IWyrdRuntime runtime,
        CancellationToken cancellationToken)
    {
        await runtime.PublishAsync(notice, cancellationToken);
        return Results.Accepted();
    }
}

public sealed record ReconcilePaymentEndpoint() : Post("/subscriptions/{id}/reconcile/{review:int}")
{
    public static async Task<IResult> HandleAsync(
        string id,
        int review,
        [FromServices] IWyrdRuntime runtime,
        CancellationToken cancellationToken)
    {
        var offer = await SubscriptionLookup.FindOfferAsync(runtime, id, cancellationToken);
        if (offer is null)
        {
            return Results.NotFound();
        }

        var command = new ReconcilePayment(offer, review);
        var decision = await runtime.SendAsync(command, cancellationToken);
        return DecisionHttpResult.From(decision);
    }
}

public sealed record ResumeCollectionEndpoint() : Post("/operators/payment/{id}/resume")
{
    public static async Task<IResult> HandleAsync(
        string id,
        [FromServices] IWyrdRuntime runtime,
        CancellationToken cancellationToken)
    {
        await runtime.ResumeAsync(Flows.PaymentCollection, id, cancellationToken);
        return Results.Accepted();
    }
}
