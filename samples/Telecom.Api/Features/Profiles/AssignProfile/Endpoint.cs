using Microsoft.AspNetCore.Mvc;
using MinApiLib.Endpoints;
using Wyrd.Runtime;
using Telecom.Api.Shared;

namespace Telecom.Api.Features.Profiles;

public sealed record AssignProfileEndpoint() : Post("/subscriptions/{id}/profiles/{profile}/attempts/{attempt:int}")
{
    public static async Task<IResult> HandleAsync(
        string id,
        string profile,
        int attempt,
        [FromServices] IWyrdRuntime runtime,
        CancellationToken cancellationToken)
    {
        var offer = await SubscriptionLookup.FindOfferAsync(runtime, id, cancellationToken);
        if (offer is null)
        {
            return Results.NotFound();
        }

        var command = new AssignProfile(id, offer.CustomerId, profile, attempt);
        var decision = await runtime.SendAsync(command, cancellationToken);
        return DecisionHttpResult.From(decision);
    }
}
