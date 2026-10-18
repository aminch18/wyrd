using Npgsql;
using Shop;
using Shop.Api;
using Shop.Contracts;
using Shop.Fulfillment;
using Shop.Inventory;
using Shop.Orders;
using Shop.Payments;
using Wyrd;
using Wyrd.PostgreSql;
using Wyrd.Runtime;

var builder = WebApplication.CreateBuilder(args);

// The host is configuration: with a connection string the shop is durable on PostgreSQL (Verdandi); without one it
// runs in memory. The endpoints below and the whole domain are the same either way.
var connectionString = builder.Configuration.GetConnectionString("Wyrd");
if (connectionString is null)
{
    builder.Services.AddSingleton(_ => ShopModule.CreateRuntime());
    builder.Services.AddSingleton<IWyrdRuntime>(services => services.GetRequiredService<InMemoryRuntime>());
    builder.Services.AddHostedService<WyrdClock>();
}
else
{
    builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
    builder.Services.AddSingleton(services => ShopModule.Register(new PostgresRuntime(
        services.GetRequiredService<NpgsqlDataSource>(),
        ShopJson.Default,
        logger: services.GetRequiredService<ILogger<PostgresRuntime>>())));
    builder.Services.AddSingleton<IWyrdRuntime>(services => services.GetRequiredService<PostgresRuntime>());
    builder.Services.AddHostedService<WyrdWorker>();
}

var app = builder.Build();

// ── Commands: an endpoint turns HTTP into a command and the decision back into HTTP ──────────

app.MapPost("/stock/{sku}", async (string sku, RestockRequest request, IWyrdRuntime wyrd) =>
    ToHttp(await wyrd.SendAsync(new Restock(sku, request.Quantity)), $"/stock/{sku}"));

app.MapPost("/orders", async (PlaceOrderRequest request, IWyrdRuntime wyrd) =>
    ToHttp(await wyrd.SendAsync(new PlaceOrder(request.OrderId, request.Sku, request.Quantity, request.Amount)), $"/orders/{request.OrderId}"));

app.MapPost("/orders/{orderId}/lines", async (string orderId, AddLineRequest request, IWyrdRuntime wyrd) =>
    ToHttp(await wyrd.SendAsync(new AddLine(orderId, request.LineId, request.Sku, request.Quantity, request.Amount, request.Currency)), $"/orders/{orderId}"));

app.MapDelete("/orders/{orderId}/lines/{lineId}", async (string orderId, string lineId, IWyrdRuntime wyrd) =>
    ToHttp(await wyrd.SendAsync(new CancelLine(orderId, lineId)), $"/orders/{orderId}"));

app.MapPost("/orders/{orderId}/submit", async (string orderId, IWyrdRuntime wyrd) =>
    ToHttp(await wyrd.SendAsync(new SubmitOrder(orderId)), $"/orders/{orderId}"));

app.MapPost("/orders/{orderId}/cancel", async (string orderId, IWyrdRuntime wyrd) =>
    ToHttp(await wyrd.SendAsync(new CancelDraftOrder(orderId)), $"/orders/{orderId}"));

// ── Facts from other systems: the payment provider's webhook ────────────────────────────────

app.MapPost("/webhooks/payments", async (PaymentWebhook webhook, IWyrdRuntime wyrd) =>
{
    await wyrd.PublishAsync(new PaymentConfirmed(webhook.OrderId));
    return Results.Accepted();
});

// ── Queries: read models and state, no logic ────────────────────────────────────────────────

app.MapGet("/orders", (IWyrdRuntime wyrd) => wyrd.ViewsAsync<OrderSummary>());

app.MapGet("/order-details", (IWyrdRuntime wyrd) => wyrd.ViewsAsync<OrderDetail>());

app.MapGet("/orders/{orderId}", async (string orderId, IWyrdRuntime wyrd) =>
    await wyrd.ViewAsync<OrderDetail>(orderId) is { } details
        ? Results.Ok(new
        {
            order = await wyrd.ViewAsync<OrderSummary>(orderId),
            details,
            fulfillment = (await wyrd.StatusOfAsync(Processes.OrderFulfillment, orderId))?.ToString(),
        })
        : Results.NotFound());

app.MapGet("/stock/{sku}", async (string sku, IWyrdRuntime wyrd) =>
    new { sku, available = (await wyrd.StateOfAsync<StockItem>(sku)).Available });

// ── Developer tools, in memory only: see what happened, break things on purpose, travel in time ──

if (connectionString is null)
{
    var dev = app.MapGroup("/dev");

    dev.MapGet("/journal", (InMemoryRuntime wyrd) => wyrd.Journal.Select(entry => entry.ToString()));

    dev.MapGet("/published", (InMemoryRuntime wyrd) => wyrd.Published);

    dev.MapPost("/payment-gateway/fail", (int times, InMemoryRuntime wyrd) =>
    {
        wyrd.FailNext<ChargePayment>(times);
        return Results.NoContent();
    });

    dev.MapPost("/orders/{orderId}/resume", async (string orderId, InMemoryRuntime wyrd) =>
    {
        await wyrd.ResumeAsync(Processes.OrderFulfillment, orderId);
        return Results.NoContent();
    });

    dev.MapPost("/orders/{orderId}/compensate", async (string orderId, InMemoryRuntime wyrd) =>
    {
        await wyrd.CompensateAsync(Processes.OrderFulfillment, orderId);
        return Results.NoContent();
    });

    dev.MapPost("/clock/advance", async (int minutes, InMemoryRuntime wyrd) =>
    {
        await wyrd.AdvanceAsync(TimeSpan.FromMinutes(minutes));
        return Results.Ok(new { now = wyrd.Now.ToString() });
    });
}

app.Run();

static IResult ToHttp(Decision decision, string location) => decision switch
{
    Decision.Accepted => Results.Accepted(location),
    Decision.Rejected rejected => Results.Problem(title: rejected.Error.ToString(), statusCode: StatusCodes.Status409Conflict),
    _ => Results.StatusCode(StatusCodes.Status500InternalServerError),
};
