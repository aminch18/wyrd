using Telecom;
using Telecom.Hosting;
using Wyrd;
using Wyrd.EntityFrameworkCore;
using Wyrd.Runtime;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddTelecom(builder.Configuration);
var app = builder.Build();

app.MapPost("/subscriptions", async (Offer offer, IWyrdRuntime runtime, CancellationToken ct) =>
    Answer(await runtime.SendAsync(new ContractSubscription(offer), ct)));
app.MapGet("/subscriptions/{id}", async (string id, IWyrdRuntime runtime, CancellationToken ct) =>
    await runtime.ViewAsync<CustomerProgress>(id, ct) is { } view ? Results.Ok(view) : Results.NotFound());
app.MapGet("/operations/{id}", async (string id, IWyrdRuntime runtime, CancellationToken ct) => Results.Ok(new
{
    Port = await runtime.ViewAsync<PortabilityDetail>(id, ct),
    PaymentAndBilling = await runtime.ViewAsync<PaymentBillingStatus>(id, ct),
    Capture = await runtime.StatusOfAsync(Flows.PaymentCollection, id, ct),
    PortSubmission = await PortStatus(runtime, id, ct),
    Profile = await ProfileStatus(runtime, id, ct),
}));
app.MapGet("/incidents", async (IWyrdRuntime runtime, CancellationToken ct) =>
    Results.Ok((await runtime.ViewsAsync<IncidentItem>(ct)).Where(i => i.Open)));
app.MapGet("/health/queue", async (EfCoreRuntime<TelecomDbContext> runtime, CancellationToken ct) => Results.Ok(await runtime.HealthAsync(ct)));
app.MapPost("/webhooks/payments", async (PaymentConfirmedV1 notice, IWyrdRuntime runtime, CancellationToken ct) =>
{
    await runtime.PublishAsync(notice, ct); return Results.Accepted();
});
app.MapPost("/webhooks/mnp/confirmed", async (PortConfirmedV1 notice, IWyrdRuntime runtime, CancellationToken ct) =>
{
    await runtime.PublishAsync(notice, ct); return Results.Accepted();
});
app.MapPost("/webhooks/mnp/completed", async (PortCompletedV1 notice, IWyrdRuntime runtime, CancellationToken ct) =>
{
    await runtime.PublishAsync(notice, ct); return Results.Accepted();
});
app.MapPost("/webhooks/mnp/cancelled", async (PortCancelledV1 notice, IWyrdRuntime runtime, CancellationToken ct) =>
{
    await runtime.PublishAsync(notice, ct); return Results.Accepted();
});
app.MapPost("/webhooks/connectivity", async (ConnectivityVerifiedV1 notice, IWyrdRuntime runtime, CancellationToken ct) =>
{
    await runtime.PublishAsync(notice, ct); return Results.Accepted();
});
app.MapPost("/subscriptions/{id}/cancel-port", async (string id, IWyrdRuntime runtime, CancellationToken ct) =>
    await OfferOf(runtime, id, ct) is { } offer
        ? Answer(await runtime.SendAsync(new RequestPortCancellation(id, offer.Msisdn, offer.Epoch), ct)) : Results.NotFound());
app.MapPost("/subscriptions/{id}/profiles/{profile}/attempts/{attempt:int}", async (string id, string profile, int attempt, IWyrdRuntime runtime, CancellationToken ct) =>
    await OfferOf(runtime, id, ct) is { } offer
        ? Answer(await runtime.SendAsync(new AssignProfile(id, offer.CustomerId, profile, attempt), ct)) : Results.NotFound());
app.MapPost("/subscriptions/{id}/reconcile/{review:int}", async (string id, int review, IWyrdRuntime runtime, CancellationToken ct) =>
    await OfferOf(runtime, id, ct) is { } offer
        ? Answer(await runtime.SendAsync(new ReconcilePayment(offer, review), ct)) : Results.NotFound());
app.MapPost("/operators/payment/{id}/resume", async (string id, IWyrdRuntime runtime, CancellationToken ct) =>
{
    await runtime.ResumeAsync(Flows.PaymentCollection, id, ct); return Results.Accepted();
});
app.MapGet("/histories/profiles/{id}/{profile}/{attempt:int}", async (string id, string profile, int attempt, IWyrdRuntime runtime, CancellationToken ct) =>
    await runtime.HistoryOfAsync(Flows.ProfileProvisioning, Flows.ProfileKey(id, profile, attempt), ct) is { } history
        ? Results.Text(history.ToJson(), "application/json") : Results.NotFound());
app.Run();

static IResult Answer(Decision decision) => decision is Decision.Accepted ? Results.Accepted() : Results.Conflict(new { Error = ((Decision.Rejected)decision).Error.ToString() });
static async Task<Offer?> OfferOf(IWyrdRuntime runtime, string id, CancellationToken ct) =>
    await runtime.StateOfAsync<Subscription>(id, ct) is Subscription.Contracted contracted ? contracted.Offer : null;

static async Task<ProcessStatus?> ProfileStatus(IWyrdRuntime runtime, string id, CancellationToken ct)
{
    var view = await runtime.ViewAsync<CustomerProgress>(id, ct);
    return view?.ProfileId is { } profile ? await runtime.StatusOfAsync(Flows.ProfileProvisioning, Flows.ProfileKey(id, profile, view.Attempt), ct) : null;
}

static async Task<ProcessStatus?> PortStatus(IWyrdRuntime runtime, string id, CancellationToken ct)
{
    var offer = await OfferOf(runtime, id, ct);
    return offer is null ? null : await runtime.StatusOfAsync(Flows.PortSubmission, Flows.PortKey(id, offer.Msisdn, offer.Epoch), ct);
}
