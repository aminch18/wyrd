using Microsoft.AspNetCore.Mvc;
using MinApiLib.Endpoints;
using Wyrd.Runtime;
using Telecom.Api.Shared;

namespace Telecom.Api.Features.Portability;

public sealed record RequestCancellationEndpoint() : Post("/subscriptions/{id}/cancel-port")
{
    public static async Task<IResult> HandleAsync(
        string id,
        [FromServices] IWyrdRuntime runtime,
        CancellationToken cancellationToken)
    {
        var offer = await SubscriptionLookup.FindOfferAsync(runtime, id, cancellationToken);
        if (offer is null)
        {
            return Results.NotFound();
        }

        var command = new RequestPortCancellation(id, offer.Msisdn, offer.Epoch);
        var decision = await runtime.SendAsync(command, cancellationToken);
        return DecisionHttpResult.From(decision);
    }
}
