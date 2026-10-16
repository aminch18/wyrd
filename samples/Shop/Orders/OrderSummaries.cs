using Shop.Inventory;
using Shop.Payments;
using Shop.Shipping;
using Wyrd;

namespace Shop.Orders;

/// <summary>Read model for the orders screen: one row per order, shaped for display.</summary>
public sealed record OrderSummary(string OrderId, string Sku, int Quantity, decimal Amount, string Status);

/// <summary>Keeps <see cref="OrderSummary"/> up to date from what happens across the shop.</summary>
public sealed class OrderSummaries : Projection<OrderSummary>
{
    public OrderSummaries()
    {
        On<OrderPlaced>(e => e.OrderId, (_, e) => new OrderSummary(e.OrderId, e.Sku, e.Quantity, e.Amount, "Placed"));
        On<StockReserved>(e => e.OrderId, (view, _) => view is null ? null : view with { Status = "Reserved" });
        On<PaymentCharged>(e => e.OrderId, (view, _) => view is null ? null : view with { Status = "Paid" });
        On<OrderShipped>(e => e.OrderId, (view, _) => view is null ? null : view with { Status = "Shipped" });
        On<PaymentRefunded>(e => e.OrderId, (view, _) => view is null ? null : view with { Status = "Refunded" });
        On<StockReleased>(e => e.OrderId, (view, _) => view is null ? null : view with { Status = "Cancelled" });
    }
}
