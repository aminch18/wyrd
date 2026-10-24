using Shop.Inventory;
using Shop.Orders;
using Shop.Payments;
using Shop.Shipping;
using Wyrd.Testing;

namespace Shop.Tests;

public class OrdersScreenSpecs
{
    [Fact]
    public void a_shipped_order_shows_as_shipped() =>
        Scenario.For(new OrderSummaries())
            .Given(
                new OrderPlaced("A-1", "KEYBOARD", 1, 89m),
                new StockReserved("KEYBOARD", 1, "A-1"),
                new PaymentCharged("A-1", 89m),
                new OrderShipped("A-1", "KEYBOARD"))
            .Then("A-1", new OrderSummary("A-1", "KEYBOARD", 1, 89m, "Shipped"));

    [Fact]
    public void a_cancelled_order_shows_as_cancelled() =>
        Scenario.For(new OrderSummaries())
            .Given(
                new OrderPlaced("A-1", "KEYBOARD", 1, 89m),
                new StockReserved("KEYBOARD", 1, "A-1"),
                new StockReleased("KEYBOARD", 1, "A-1"),
                new OrderCancelled("A-1"))
            .Then("A-1", new OrderSummary("A-1", "KEYBOARD", 1, 89m, "Cancelled"));

    [Fact]
    public void a_late_event_never_moves_an_order_back() =>
        Scenario.For(new OrderSummaries())
            .Given(
                new OrderPlaced("A-1", "KEYBOARD", 1, 89m),
                new OrderCancelled("A-1"),
                new StockReleased("KEYBOARD", 1, "A-1")) // arrives late
            .Then("A-1", new OrderSummary("A-1", "KEYBOARD", 1, 89m, "Cancelled"));
}
