using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MinApiLib.Endpoints;
using Telecom.Providers.Persistence;

namespace Telecom.Providers.Features.ExecuteOperation;

public sealed record ExecuteOperationEndpoint() : Post("/operations")
{
    public static async Task<IResult> HandleAsync(
        [FromBody] ProviderRequest request,
        HttpRequest http,
        [FromServices] ProviderSettings settings,
        [FromServices] IDbContextFactory<ProviderDb> contexts,
        CancellationToken cancellationToken)
    {
        if (!Validate(request, http, settings, out var deliveryKey))
        {
            return Results.BadRequest();
        }

        // Concurrent workers race on unique logical/resource keys. The losing insert rereads the winner.
        for (var retry = 0; retry < 3; retry++)
        {
            await using var database = await contexts.CreateDbContextAsync(cancellationToken);
            var existing = await database.Effects.FindAsync([request.LogicalKey], cancellationToken);

            if (request.Action == ProviderAction.FindCapture)
            {
                return FindCapture(existing);
            }

            var serializedRequest = JsonSerializer.Serialize(request, TelecomJson.Default.ProviderRequest);
            if (existing is not null)
            {
                return ReplayReceipt(existing, serializedRequest);
            }

            string? reservedResource = null;
            if (request.Action == ProviderAction.Provision)
            {
                reservedResource = request.Resource;
                var alreadyReserved = await database.Effects.AnyAsync(
                    effect => effect.Resource == reservedResource, cancellationToken);
                if (alreadyReserved)
                {
                    return Results.Ok(new ProviderReply(null, ProviderError.Conflict));
                }
            }

            var effect = new ProviderEffect
            {
                Key = request.LogicalKey,
                Request = serializedRequest,
                Resource = reservedResource,
                Receipt = $"receipt-{request.LogicalKey}",
                DeliveryKey = deliveryKey,
            };
            database.Effects.Add(effect);

            try
            {
                await database.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException) when (retry < 2)
            {
                continue;
            }

            // The effect is durable already. A 503 demonstrates a commit with no usable reply.
            if (ShouldLoseReply(settings, request.Action))
            {
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            return Results.Ok(new ProviderReply(effect.Receipt));
        }

        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }

    private static bool Validate(ProviderRequest request, HttpRequest http, ProviderSettings settings, out Guid deliveryKey)
    {
        var expectedProvider = request.Action switch
        {
            ProviderAction.Capture or ProviderAction.FindCapture => "payments",
            ProviderAction.Provision => "esim",
            _ => "mnp",
        };

        deliveryKey = default;
        if (expectedProvider != settings.Name)
        {
            return false;
        }

        return Guid.TryParse(http.Headers["Idempotency-Key"], out deliveryKey);
    }

    private static IResult FindCapture(ProviderEffect? existing)
    {
        if (existing is null)
        {
            return Results.Ok(new ProviderReply(null, ProviderError.NotFound));
        }

        return Results.Ok(new ProviderReply(existing.Receipt));
    }

    private static IResult ReplayReceipt(ProviderEffect existing, string serializedRequest)
    {
        if (existing.Request != serializedRequest)
        {
            return Results.Ok(new ProviderReply(null, ProviderError.Conflict));
        }

        return Results.Ok(new ProviderReply(existing.Receipt));
    }

    private static bool ShouldLoseReply(ProviderSettings settings, ProviderAction action)
    {
        return settings.LoseFirstReply && action is ProviderAction.Capture or ProviderAction.Provision;
    }
}
