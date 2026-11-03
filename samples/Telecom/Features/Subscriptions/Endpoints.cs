using Microsoft.AspNetCore.Mvc;
using MinApiLib.Endpoints;
using Telecom.Shared;
using Wyrd.Runtime;

namespace Telecom.Features.Subscriptions;

public sealed record ContractSubscriptionEndpoint() : Post("/subscriptions")
{
    public static async Task<IResult> HandleAsync(
        [FromBody] Offer offer,
        [FromServices] IWyrdRuntime runtime,
        CancellationToken cancellationToken)
    {
        var command = new ContractSubscription(offer);
        var decision = await runtime.SendAsync(command, cancellationToken);
        return DecisionHttpResult.From(decision);
    }
}

public sealed record GetProgressEndpoint() : Get("/subscriptions/{id}")
{
    public static async Task<IResult> HandleAsync(
        string id,
        [FromServices] IWyrdRuntime runtime,
        CancellationToken cancellationToken)
    {
        var progress = await runtime.ViewAsync<CustomerProgress>(id, cancellationToken);
        if (progress is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(progress);
    }
}
