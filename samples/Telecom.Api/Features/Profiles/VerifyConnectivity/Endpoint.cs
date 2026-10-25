using Microsoft.AspNetCore.Mvc;
using MinApiLib.Endpoints;
using Wyrd.Runtime;

namespace Telecom.Api.Features.Profiles;

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
