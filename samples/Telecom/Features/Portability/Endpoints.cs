using Microsoft.AspNetCore.Mvc;
using MinApiLib.Endpoints;
using Telecom.Features.Subscriptions;
using Telecom.Shared;
using Wyrd.Runtime;

namespace Telecom.Features.Portability;

public sealed record CompletePortEndpoint() : Post("/webhooks/mnp/completed")
{
    public static async Task<IResult> HandleAsync(
        [FromBody] PortCompletedV1 notice,
        [FromServices] IWyrdRuntime runtime,
        CancellationToken cancellationToken)
    {
        if (!PortNoticeInput.IsValid(notice.SubscriptionId, notice.Msisdn, notice.Epoch))
        {
            return Results.BadRequest();
        }

        await runtime.PublishAsync(notice, cancellationToken);
        return Results.Accepted();
    }
}

public sealed record ConfirmCancellationEndpoint() : Post("/webhooks/mnp/cancelled")
{
    public static async Task<IResult> HandleAsync(
        [FromBody] PortCancelledV1 notice,
        [FromServices] IWyrdRuntime runtime,
        CancellationToken cancellationToken)
    {
        if (!PortNoticeInput.IsValid(notice.SubscriptionId, notice.Msisdn, notice.Epoch))
        {
            return Results.BadRequest();
        }

        await runtime.PublishAsync(notice, cancellationToken);
        return Results.Accepted();
    }
}

public sealed record ConfirmPortEndpoint() : Post("/webhooks/mnp/confirmed")
{
    public static async Task<IResult> HandleAsync(
        [FromBody] PortConfirmedV1 notice,
        [FromServices] IWyrdRuntime runtime,
        CancellationToken cancellationToken)
    {
        if (!PortNoticeInput.IsValid(notice.SubscriptionId, notice.Msisdn, notice.Epoch) || notice.WindowEnd <= notice.WindowStart)
        {
            return Results.BadRequest();
        }

        await runtime.PublishAsync(notice, cancellationToken);
        return Results.Accepted();
    }
}

public sealed record RequestCancellationEndpoint() : Post("/subscriptions/{id}/cancel-port")
{
    public static async Task<IResult> HandleAsync(
        string id,
        [FromServices] IWyrdRuntime runtime,
        CancellationToken cancellationToken)
    {
        var offer = await SubscriptionLookup.FindOfferAsync(runtime, id, cancellationToken);
        if (offer is null)
        {
            return Results.NotFound();
        }

        var command = new RequestPortCancellation(id, offer.Msisdn, offer.Epoch);
        var decision = await runtime.SendAsync(command, cancellationToken);
        return DecisionHttpResult.From(decision);
    }
}

internal static class PortNoticeInput
{
    public static bool IsValid(string? subscriptionId, string? msisdn, long epoch) =>
        Offer.ValidId(subscriptionId) && Offer.ValidMsisdn(msisdn) && epoch > 0;
}
