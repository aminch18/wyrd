using Shop;
using Shop.Api;
using Shop.Contracts;
using Shop.Fulfillment;
using Shop.Inventory;
using Shop.Orders;
using Shop.Payments;
using Wyrd;
using Wyrd.Runtime;

var builder = WebApplication.CreateBuilder(args);

// Wyrd runs in memory here: same engine and guarantees as production, without a database or a broker yet.
// The day PostgreSQL and RabbitMQ arrive, this line changes; the endpoints and the domain do not.
builder.Services.AddSingleton(_ => ShopModule.CreateRuntime());
builder.Services.AddHostedService<WyrdClock>();

var app = builder.Build();

// ── Commands: an endpoint turns HTTP into a command and the decision back into HTTP ──────────

app.MapPost("/stock/{sku}", async (string sku, RestockRequest request, InMemoryRuntime wyrd) =>
    ToHttp(await wyrd.SendAsync(new Restock(sku, request.Quantity)), $"/stock/{sku}"));

app.MapPost("/orders", async (PlaceOrderRequest request, InMemoryRuntime wyrd) =>
    ToHttp(await wyrd.SendAsync(new PlaceOrder(request.OrderId, request.Sku, request.Quantity, request.Amount)), $"/orders/{request.OrderId}"));

// ── Facts from other systems: the payment provider's webhook ────────────────────────────────

app.MapPost("/webhooks/payments", async (PaymentWebhook webhook, InMemoryRuntime wyrd) =>
{
    await wyrd.PublishAsync(new PaymentConfirmed(webhook.OrderId));
    return Results.Accepted();
});

// ── Queries: read models and state, no logic ────────────────────────────────────────────────

app.MapGet("/orders", (InMemoryRuntime wyrd) => wyrd.Views<OrderSummary>());

app.MapGet("/orders/{orderId}", (string orderId, InMemoryRuntime wyrd) =>
    wyrd.View<OrderSummary>(orderId) is { } order
        ? Results.Ok(new { order, fulfillment = wyrd.StatusOf(Processes.OrderFulfillment, orderId)?.ToString() })
        : Results.NotFound());

app.MapGet("/stock/{sku}", (string sku, InMemoryRuntime wyrd) =>
    new { sku, available = wyrd.StateOf<StockItem>(sku).Available });

// ── Developer tools: see what happened, break things on purpose, travel in time ─────────────

app.MapGet("/dev/journal", (InMemoryRuntime wyrd) => wyrd.Journal.Select(entry => entry.ToString()));

app.MapGet("/dev/published", (InMemoryRuntime wyrd) => wyrd.Published);

app.MapPost("/dev/payment-gateway/fail", (int times, InMemoryRuntime wyrd) =>
{
    wyrd.FailNext<ChargePayment>(times);
    return Results.NoContent();
});

app.MapPost("/dev/orders/{orderId}/resume", async (string orderId, InMemoryRuntime wyrd) =>
{
    await wyrd.ResumeAsync(Processes.OrderFulfillment, orderId);
    return Results.NoContent();
});

app.MapPost("/dev/orders/{orderId}/compensate", async (string orderId, InMemoryRuntime wyrd) =>
{
    await wyrd.CompensateAsync(Processes.OrderFulfillment, orderId);
    return Results.NoContent();
});

app.MapPost("/dev/clock/advance", async (int minutes, InMemoryRuntime wyrd) =>
{
    await wyrd.AdvanceAsync(TimeSpan.FromMinutes(minutes));
    return Results.Ok(new { now = wyrd.Now.ToString() });
});

app.Run();

static IResult ToHttp(Decision decision, string location) => decision switch
{
    Decision.Accepted => Results.Accepted(location),
    Decision.Rejected rejected => Results.Problem(title: rejected.Error.ToString(), statusCode: StatusCodes.Status409Conflict),
    _ => Results.StatusCode(StatusCodes.Status500InternalServerError),
};
