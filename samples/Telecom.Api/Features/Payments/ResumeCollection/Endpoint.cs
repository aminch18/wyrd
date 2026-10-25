using Microsoft.AspNetCore.Mvc;
using MinApiLib.Endpoints;
using Wyrd.Runtime;

namespace Telecom.Api.Features.Payments;

public sealed record ResumeCollectionEndpoint() : Post("/operators/payment/{id}/resume")
{
    public static async Task<IResult> HandleAsync(
        string id,
        [FromServices] IWyrdRuntime runtime,
        CancellationToken cancellationToken)
    {
        await runtime.ResumeAsync(Flows.PaymentCollection, id, cancellationToken);
        return Results.Accepted();
    }
}
