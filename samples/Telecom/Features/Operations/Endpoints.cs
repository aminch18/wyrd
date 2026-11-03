using Microsoft.AspNetCore.Mvc;
using MinApiLib.Endpoints;
using Telecom.Features.Subscriptions;
using Telecom.Infrastructure;
using Telecom.Shared;
using Wyrd.EntityFrameworkCore;
using Wyrd.Runtime;

namespace Telecom.Features.Operations;

public sealed record GetOperationEndpoint() : Get("/operations/{id}")
{
    public static async Task<IResult> HandleAsync(
        string id,
        [FromServices] IWyrdRuntime runtime,
        CancellationToken cancellationToken)
    {
        var port = await runtime.ViewAsync<PortabilityDetail>(id, cancellationToken);
        var paymentAndBilling = await runtime.ViewAsync<PaymentBillingStatus>(id, cancellationToken);
        var captureStatus = await runtime.StatusOfAsync(Flows.PaymentCollection, id, cancellationToken);
        var portStatus = await FindPortStatusAsync(runtime, id, cancellationToken);
        var profileStatus = await FindProfileStatusAsync(runtime, id, cancellationToken);

        var response = new OperationDetail(port, paymentAndBilling, captureStatus, portStatus, profileStatus);
        return Results.Ok(response);
    }

    private static async Task<ProcessStatus?> FindPortStatusAsync(IWyrdRuntime runtime, string id, CancellationToken cancellationToken)
    {
        var offer = await SubscriptionLookup.FindOfferAsync(runtime, id, cancellationToken);
        if (offer is null)
        {
            return null;
        }

        var key = Flows.PortKey(id, offer.Msisdn, offer.Epoch);
        return await runtime.StatusOfAsync(Flows.PortSubmission, key, cancellationToken);
    }

    private static async Task<ProcessStatus?> FindProfileStatusAsync(IWyrdRuntime runtime, string id, CancellationToken cancellationToken)
    {
        var progress = await runtime.ViewAsync<CustomerProgress>(id, cancellationToken);
        if (progress?.ProfileId is null)
        {
            return null;
        }

        var key = Flows.ProfileKey(id, progress.ProfileId, progress.Attempt);
        return await runtime.StatusOfAsync(Flows.ProfileProvisioning, key, cancellationToken);
    }
}

public sealed record OperationDetail(
    PortabilityDetail? Port,
    PaymentBillingStatus? PaymentAndBilling,
    ProcessStatus? Capture,
    ProcessStatus? PortSubmission,
    ProcessStatus? Profile);

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
