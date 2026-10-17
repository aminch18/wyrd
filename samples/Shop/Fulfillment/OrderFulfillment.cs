using Shop.Contracts;
using Shop.Inventory;
using Shop.Orders;
using Shop.Payments;
using Shop.Shipping;
using Wyrd;
using Wyrd.Processes;

namespace Shop.Fulfillment;

/// <summary>The business process, end to end. This is what a reviewer reads.</summary>
public static class Processes
{
    // Every step that can be undone retries, because its undo uses the same policy: a compensation that gives up
    // after one transient failure leaves stock reserved forever. Skuld found exactly that.
    public static Process OrderFulfillment => Process
        .StartOn<OrderPlaced>(e => e.OrderId)
        .Then(Stock.Reserve).Retry(5.Times).Undo(Stock.Release)
        .Then(Payments.Charge).Retry(5.Times, Backoff.Exponential).Undo(Payments.Refund)
        .Await<PaymentConfirmed>(e => e.OrderId).Within(15.Minutes)
        .Then(Shipping.Ship).Retry(5.Times)
        .OnCompensated(Orders.Cancel).Retry(5.Times);
}

// Steps are pure functions: the order that started the process -> the next command.

internal static class Orders
{
    public static CancelOrder Cancel(OrderPlaced order) => new(order.OrderId);
}

internal static class Stock
{
    public static ReserveStock Reserve(OrderPlaced order) => new(order.Sku, order.Quantity, order.OrderId);

    public static ReleaseStock Release(OrderPlaced order) => new(order.Sku, order.Quantity, order.OrderId);
}

internal static class Payments
{
    public static ChargePayment Charge(OrderPlaced order) => new(order.OrderId, order.Amount);

    public static RefundPayment Refund(OrderPlaced order) => new(order.OrderId);
}

internal static class Shipping
{
    public static ShipOrder Ship(OrderPlaced order) => new(order.OrderId);
}
