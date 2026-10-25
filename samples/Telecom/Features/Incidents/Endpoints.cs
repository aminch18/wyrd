using Microsoft.AspNetCore.Mvc;
using MinApiLib.Endpoints;
using Wyrd.Runtime;

namespace Telecom.Features.Incidents;

public sealed record ListIncidentsEndpoint() : Get("/incidents")
{
    public static async Task<IResult> HandleAsync(
        [FromServices] IWyrdRuntime runtime,
        CancellationToken cancellationToken)
    {
        var incidents = await runtime.ViewsAsync<IncidentItem>(cancellationToken);
        var openIncidents = incidents.Where(incident => incident.Open);
        return Results.Ok(openIncidents);
    }
}
