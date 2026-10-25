using Microsoft.AspNetCore.Mvc;
using MinApiLib.Endpoints;
using Telecom.Features.Subscriptions;
using Telecom.Shared;
using Wyrd.Runtime;

namespace Telecom.Features.Profiles;

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

public sealed record GetHistoryEndpoint() : Get("/histories/profiles/{id}/{profile}/{attempt:int}")
{
    public static async Task<IResult> HandleAsync(
        string id,
        string profile,
        int attempt,
        [FromServices] IWyrdRuntime runtime,
        CancellationToken cancellationToken)
    {
        var key = Flows.ProfileKey(id, profile, attempt);
        var history = await runtime.HistoryOfAsync(Flows.ProfileProvisioning, key, cancellationToken);
        if (history is null)
        {
            return Results.NotFound();
        }

        return Results.Text(history.ToJson(), "application/json");
    }
}

public sealed record VerifyConnectivityEndpoint() : Post("/webhooks/connectivity")
{
    public static async Task<IResult> HandleAsync(
        [FromBody] ConnectivityVerifiedV1 notice,
        [FromServices] IWyrdRuntime runtime,
        CancellationToken cancellationToken)
    {
        await runtime.PublishAsync(notice, cancellationToken);
        return Results.Accepted();
    }
}
