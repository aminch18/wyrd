using Microsoft.AspNetCore.Mvc;
using MinApiLib.Endpoints;
using Wyrd.Runtime;

namespace Telecom.Api.Features.Profiles;

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
