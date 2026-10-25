using Microsoft.AspNetCore.Mvc;
using MinApiLib.Endpoints;
using Wyrd.Runtime;

namespace Telecom.Api.Features.Payments;

public sealed record ConfirmPaymentEndpoint() : Post("/webhooks/payments")
{
    public static async Task<IResult> HandleAsync(
        [FromBody] PaymentConfirmedV1 notice,
        [FromServices] IWyrdRuntime runtime,
        CancellationToken cancellationToken)
    {
        await runtime.PublishAsync(notice, cancellationToken);
        return Results.Accepted();
    }
}
