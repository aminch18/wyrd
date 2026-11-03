using Microsoft.AspNetCore.Mvc;
using MinApiLib.Endpoints;
using Wyrd.Runtime;
using Telecom.Shared;
using Telecom.Features.Subscriptions;

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

public sealed record ResolveIncidentEndpoint() : Post("/incidents/{id}/{kind}/{attempt:int}/resolve")
{
    public static async Task<IResult> HandleAsync(string id, string kind, int attempt, [FromServices] IWyrdRuntime runtime, CancellationToken cancellationToken)
    {
        var decision = await runtime.SendAsync(new ResolveIncident(id, kind, attempt), cancellationToken);
        return DecisionHttpResult.From(decision);
    }
}

public sealed record OpenIncidentEndpoint() : Post("/incidents/{id}/{kind}/{attempt:int}/open")
{
    public static async Task<IResult> HandleAsync(string id, string kind, int attempt, [FromServices] IWyrdRuntime runtime, CancellationToken cancellationToken)
    {
        var offer = await SubscriptionLookup.FindOfferAsync(runtime, id, cancellationToken);
        if (offer is null)
        {
            return Results.NotFound();
        }

        var decision = await runtime.SendAsync(new OpenIncident(id, kind, attempt), cancellationToken);
        return DecisionHttpResult.From(decision);
    }
}
