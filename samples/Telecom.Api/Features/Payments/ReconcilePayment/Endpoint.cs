using Microsoft.AspNetCore.Mvc;
using MinApiLib.Endpoints;
using Wyrd.Runtime;
using Telecom.Api.Shared;

namespace Telecom.Api.Features.Payments;

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
