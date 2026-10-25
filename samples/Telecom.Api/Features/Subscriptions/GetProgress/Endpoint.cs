using Microsoft.AspNetCore.Mvc;
using MinApiLib.Endpoints;
using Wyrd.Runtime;

namespace Telecom.Api.Features.Subscriptions;

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
