using Microsoft.AspNetCore.Mvc;
using MinApiLib.Endpoints;
using Wyrd.Runtime;
using Telecom.Api.Shared;

namespace Telecom.Api.Features.Subscriptions;

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
