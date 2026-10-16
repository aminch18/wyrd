using Shop.Orders;
using Wyrd.Testing;

namespace Shop.Tests;

public class OrdersSpecs
{
    [Fact]
    public void an_order_is_placed_once() =>
        Scenario.For(new PlaceOrderSlice())
            .Given(new OrderPlaced("A-1", "KEYBOARD", 1, 89m))
            .When(new PlaceOrder("A-1", "KEYBOARD", 1, 89m))
            .ThenRejected(OrderError.AlreadyPlaced);

    [Fact]
    public void a_placed_order_can_be_cancelled() =>
        Scenario.For(new CancelOrderSlice())
            .Given(new OrderPlaced("A-1", "KEYBOARD", 1, 89m))
            .When(new CancelOrder("A-1"))
            .Then(new OrderCancelled("A-1"))
            .ThenState(new Order.Cancelled());

    [Fact]
    public void an_order_is_cancelled_once() =>
        Scenario.For(new CancelOrderSlice())
            .Given(new OrderPlaced("A-1", "KEYBOARD", 1, 89m), new OrderCancelled("A-1"))
            .When(new CancelOrder("A-1"))
            .ThenRejected(OrderError.AlreadyCancelled);
}
