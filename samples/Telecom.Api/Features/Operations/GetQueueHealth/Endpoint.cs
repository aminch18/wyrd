using Microsoft.AspNetCore.Mvc;
using MinApiLib.Endpoints;
using Wyrd.Runtime;
using Telecom.Hosting;
using Wyrd.EntityFrameworkCore;

namespace Telecom.Api.Features.Operations;

public sealed record GetQueueHealthEndpoint() : Get("/health/queue")
{
    public static async Task<IResult> HandleAsync(
        [FromServices] EfCoreRuntime<TelecomDbContext> runtime,
        CancellationToken cancellationToken)
    {
        var health = await runtime.HealthAsync(cancellationToken);
        return Results.Ok(health);
    }
}
