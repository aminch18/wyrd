using Microsoft.AspNetCore.Mvc;
using MinApiLib.Endpoints;
using Wyrd.Runtime;

namespace Telecom.Api.Features.Portability;

public sealed record CompletePortEndpoint() : Post("/webhooks/mnp/completed")
{
    public static async Task<IResult> HandleAsync(
        [FromBody] PortCompletedV1 notice,
        [FromServices] IWyrdRuntime runtime,
        CancellationToken cancellationToken)
    {
        await runtime.PublishAsync(notice, cancellationToken);
        return Results.Accepted();
    }
}
