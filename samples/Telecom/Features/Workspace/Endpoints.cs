using Microsoft.AspNetCore.Mvc;
using MinApiLib.Endpoints;
using Wyrd.Runtime;

namespace Telecom.Features.Workspace;

public sealed record ListWorkspaceEndpoint() : Get("/bff/subscriptions")
{
    public static async Task<IResult> HandleAsync([FromServices] IWyrdRuntime runtime, CancellationToken cancellationToken)
    {
        var subscriptions = await WorkspaceQueries.ListAsync(runtime, cancellationToken);
        return Results.Ok(subscriptions);
    }
}

public sealed record GetWorkspaceEndpoint() : Get("/bff/subscriptions/{id}")
{
    public static async Task<IResult> HandleAsync(string id, [FromServices] IWyrdRuntime runtime, CancellationToken cancellationToken)
    {
        var workspace = await WorkspaceQueries.FindAsync(runtime, id, cancellationToken);
        if (workspace is null)
        {
            return Results.NotFound();
        }
        return Results.Ok(workspace);
    }
}
