using Microsoft.AspNetCore.Mvc;
using MinApiLib.Endpoints;
using Wyrd.Runtime;

namespace Telecom.Api.Features.Portability;

public sealed record ConfirmPortEndpoint() : Post("/webhooks/mnp/confirmed")
{
    public static async Task<IResult> HandleAsync(
        [FromBody] PortConfirmedV1 notice,
        [FromServices] IWyrdRuntime runtime,
        CancellationToken cancellationToken)
    {
        await runtime.PublishAsync(notice, cancellationToken);
        return Results.Accepted();
    }
}
