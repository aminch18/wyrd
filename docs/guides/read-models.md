# Modelling read models for screens and reports

## Start with the question the screen asks

An orders screen asks: which orders exist, what did the customer buy, how much did they pay, and where is fulfillment now? A decision asks a different question: may this command change this order? Giving both questions the same model makes a screen depend on details that belong to the write side.

In an EF Core application, you might answer the screen's question with a query joining orders, payments and shipments into a DTO. A Wyrd projection maintains a view from the facts those owners produce. The endpoint reads that view instead of rebuilding the business story on every request.

This does not make an aggregate and a view interchangeable. In the Shop sample, `Order.Placed` contains the SKU and quantity needed by order decisions. The screen also needs the price and the progress reported by inventory, payments and shipping. Those belong in its `OrderSummary`.

| Model | Question | Updated by |
| --- | --- | --- |
| Keyed domain state, such as `Order` | Is this command allowed? | Accepted events applied through `Evolve` |
| Read model, such as `OrderSummary` | What should this screen show? | A projection of relevant events |
| Process instance | What should happen next, or what needs attention? | The runtime executing the process policy |

Keep invariants on the write side. A slice decides against its state; a screen's summary is not an authoritative source for whether stock may be reserved or an order cancelled.

## Give each view a purpose

The Shop's orders screen uses this immutable row:

```csharp
public sealed record OrderSummary(
    string OrderId, string Sku, int Quantity, decimal Amount, string Status);
```

One row answers the list and detail screen's current questions. It is not a new aggregate, a public integration contract, or a complete history of the order. A finance report may need a different view, with dates, totals and currency rules specific to that report.

Use a separate view type and projection for a different purpose. The runtime identifies a stored view by its type and key. Several event handlers within one projection can update the same row; avoid registering competing projections to write the same view type.

## A projection is a set of pure updates

`Projection<TView>` registers handlers with `On<TEvent>(key, apply)`. The key selects the row. `apply` receives the current row, or null when none exists, and the event. Its return value is the next row; returning null removes it.

This small projection uses the Shop's existing events to maintain a list of open orders:

```csharp
using Shop.Orders;
using Wyrd;

public sealed record OpenOrder(string OrderId, string Sku, int Quantity);

public sealed class OpenOrders : Projection<OpenOrder>
{
    public OpenOrders()
    {
        On<OrderPlaced>(e => e.OrderId,
            (_, e) => new OpenOrder(e.OrderId, e.Sku, e.Quantity));
        On<OrderCancelled>(e => e.OrderId, (_, _) => null);
    }
}
```

`OrderPlaced` and `OrderCancelled` belong to the same order state stream, so the runtime presents them to this projection in the order they happened. The row disappears when cancellation is projected. No handler publishes a message, writes a database, or schedules another action. Wyrd stores the returned value.

Keep the updates deterministic: no wall clock, random values, HTTP calls, or mutable shared counters. If the view needs a business date, that date must come from an event or other explicit modelled input.

## Combine facts from several owners

The existing [OrderSummaries projection](../../samples/Shop/Orders/OrderSummaries.cs) subscribes to more than the order slice:

- `OrderPlaced` supplies the order details.
- `StockReserved` and `StockReleased` describe inventory progress.
- `PaymentCharged` and `PaymentRefunded` describe payment progress.
- `OrderShipped` and `OrderCancelled` describe how fulfillment ended.

The projection keys these facts by order ID even though their write-side owners use different state keys. Inventory, for example, stores stock by SKU. A read model's grouping key is chosen for its query; it does not merge the owners' consistency boundaries or put their facts into one stream.

The Shop keeps a lifecycle ordering for its display statuses. Once the row is `Cancelled`, a late `StockReleased` must not turn it back into `Released`. Once it is `Shipped`, a late payment update must not replace that final status. These are explicit rules of this screen, not a universal ranking that Wyrd assigns to events.

## Know exactly what ordering guarantees you have

Wyrd applies a projection update in the transaction that handles the event. Redelivering the same message does not apply the projection twice. That is protection against message redelivery, not a promise to merge two independently published messages that happen to contain equal data.

For domain events from **one state stream**, the runtime preserves the order in which they happened. Each carries a stream identity and version. If a later version arrives first, its read-model update is held until the gap is filled. Processes can continue while the read side waits; see the [ordered projection specifications](../../tests/Wyrd.Tests/OrderedProjectionTests.cs).

There is **no ordering guarantee across streams**. Matching order IDs do not make the order, its payment and the SKU's stock one stream. Integration events published from outside the runtime carry no internal stream metadata and are projected as they arrive.

The transaction guarantee also does not make the screen a synchronous snapshot of every owner participating in a process. A view combining several owners can show partial progress while their facts are arriving. Decide what the screen should show at those intermediate points.

## Model missing facts as well as late status changes

The Shop's `OrderSummaries` demonstrates protection against status regression after a row exists. Its status handlers leave a missing row absent: it is not a general solution for a payment event arriving before `OrderPlaced`. Do not copy that behavior into a screen that must remember every early fact.

For that requirement, keep the independent facts in the view and allow a partial row to exist. This alternative teaching example records payment or cancellation even before the order details arrive:

```csharp
using Shop.Orders;
using Shop.Payments;
using Wyrd;

public sealed record OrderCard(
    string OrderId, string? Sku, bool Paid, bool Cancelled)
{
    public string Status => Cancelled ? "Cancelled" : Paid ? "Paid" : "Placed";
}

public sealed class OrderCards : Projection<OrderCard>
{
    public OrderCards()
    {
        On<OrderPlaced>(e => e.OrderId, (view, e) =>
            (view ?? Empty(e.OrderId)) with { Sku = e.Sku });
        On<PaymentCharged>(e => e.OrderId, (view, e) =>
            (view ?? Empty(e.OrderId)) with { Paid = true });
        On<OrderCancelled>(e => e.OrderId, (view, e) =>
            (view ?? Empty(e.OrderId)) with { Cancelled = true });
    }

    private static OrderCard Empty(string orderId) => new(orderId, null, false, false);
}
```

Here, null `Sku` means that order details have not arrived yet. The screen must decide whether to show a placeholder or hide incomplete cards. Payment and cancellation flags record distinct facts; the display policy chooses cancellation when both are present. None of these updates erases an earlier fact, so these three event kinds can arrive in any order without losing information.

This example deliberately omits refunds and shipping. A production screen must model the facts and precedence its own business requires; adding another string to a status list is not automatically the right rule.

## Specify the view with Given and Then

Projection scenarios apply events in the order you give them and compare the resulting stored view, including a JSON round trip. They need no runtime or mocks.

The shipped [orders screen specifications](../../samples/Shop.Tests/OrdersScreenSpecs.cs) include this late-event regression:

```csharp
using Shop.Inventory;
using Shop.Orders;
using Wyrd.Testing;
using Xunit;

public sealed class OrdersScreenTests
{
    [Fact]
    public void a_late_release_does_not_reopen_a_cancelled_order() =>
        Scenario.For(new OrderSummaries())
            .Given(
                new OrderPlaced("A-1", "KEYBOARD", 1, 89m),
                new OrderCancelled("A-1"),
                new StockReleased("KEYBOARD", 1, "A-1"))
            .Then("A-1", new OrderSummary("A-1", "KEYBOARD", 1, 89m, "Cancelled"));
}
```

Test the alternative `OrderCards` with an event that arrives before the row's descriptive data:

```csharp
using Shop.Orders;
using Shop.Payments;
using Wyrd.Testing;
using Xunit;

public sealed class OrderCardTests
{
    [Fact]
    public void payment_can_arrive_before_order_details() =>
        Scenario.For(new OrderCards())
            .Given(
                new PaymentCharged("A-1", 89m),
                new OrderPlaced("A-1", "KEYBOARD", 1, 89m))
            .Then("A-1", new OrderCard("A-1", "KEYBOARD", true, false));
}
```

Use `.Then(key, null)` to assert that a projection deleted a row, such as a cancelled `OpenOrder`. Write separate cases for each ordering your view must tolerate.

These are tests of the projection's pure rules. `Given` does not attach stream versions, simulate the transport, or deduplicate repeated input events. Verify runtime ordering and redelivery through `InMemoryRuntime` tests; use Skuld to explore combinations of faults, redelivery and reordering for the complete system.

## Register once, query the stored view

The Shop runtime already registers `OrderSummaries` with `.AddProjection(new OrderSummaries())`. Register each additional projection when constructing the host, before processing messages. With source-generated JSON, also declare each new view type in the application's `[WyrdJsonContext]`; WYRD0300 reports missing declarations.

The current query methods are `InMemoryRuntime.View<TView>(key)` and `Views<TView>()`. A missing key returns null; the collection method returns the stored rows in creation order. Apply an explicit display sort when the UI needs a different order. These methods return materialized views, not an EF Core `IQueryable` or a pagination API.

For example, a minimal API can use the existing Shop host directly:

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Shop;
using Shop.Orders;
using Wyrd.Runtime;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(_ => ShopModule.CreateRuntime());
var app = builder.Build();

app.MapGet("/orders", (InMemoryRuntime wyrd) => wyrd.Views<OrderSummary>());
app.MapGet("/orders/{orderId}", (string orderId, InMemoryRuntime wyrd) =>
    wyrd.View<OrderSummary>(orderId) is { } order
        ? Results.Ok(order)
        : Results.NotFound());

app.Run();
```

This is a query-only host excerpt; the full [Shop API](../../samples/Shop.Api/Program.cs) also accepts commands and payment webhooks and advances the virtual clock. Keep endpoints focused on HTTP mapping and view retrieval. A request to cancel an order must still send a command to its owner; changing the displayed status is not cancellation.

There is no `IWyrdRuntime` interface in the current v0.3 API. The sample endpoints inject `InMemoryRuntime`; an abstraction for other hosts is not available merely because a future host is on the roadmap.

## Know what the current host does not provide

The in-memory host loses its data on restart; the EF Core host keeps views in the database, committed with the event that changed them. A projection added to a store that already has data catches up from the current states it names (`Seed<TState>`, see [Running durable](running-durable.md)): Wyrd keeps state, not every past event, so it is not rebuilt from all historical events, and changing a handler does not automatically migrate existing rows.

If a report needs many rows, aggregates, indexes, or filtering that `Views<TView>()` does not provide, describe that requirement explicitly. Wyrd doesn't provide a query host for reports like that. Keep the view's rules pure so a future host can execute the same model without moving business logic into the endpoint.

## Mistakes to catch in review

- **Deciding on a view.** A summary exists to answer a query. Put cancellation, stock and payment invariants in the state and decisions that own them.
- **Treating every shared key as one stream.** An order ID used to group a screen does not impose delivery order on different owners.
- **Assuming the first event creates a complete row.** Represent partial information when earlier facts can be missing, and specify how the screen handles it.
- **Putting the lifecycle in endpoints.** If every query reconstructs payment and shipping progress, the projection has not captured the screen's model.
- **One enormous view for every consumer.** Separate screens and reports can evolve independently. Share pure policy where needed, rather than making one row depend on every feature.
- **Doing I/O in `apply`.** Return the next view; let the framework persist it. A projection handler is not a place to send email or query another service.

Continue with the [Shop projection](../../samples/Shop/Orders/OrderSummaries.cs), its [screen specifications](../../samples/Shop.Tests/OrdersScreenSpecs.cs), and the [semantics of read models](../design/semantics.md#5-read-models). The [DDD migration guide](from-ddd-to-wyrd.md) explains how this read side fits alongside the domain's consistency boundaries.
