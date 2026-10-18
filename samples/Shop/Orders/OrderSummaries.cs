using Shop.Inventory;
using Shop.Payments;
using Shop.Shipping;
using Wyrd;

namespace Shop.Orders;

/// <summary>Read model for the orders screen: one row per order, shaped for display.</summary>
public sealed record OrderSummary(string OrderId, string Sku, int Quantity, decimal Amount, string Status);

/// <summary>
/// Keeps <see cref="OrderSummary"/> up to date from what happens across the shop.
/// Events from different slices can arrive in any order, so a status only moves forward: a late
/// <c>StockReleased</c> never turns a cancelled order back into a released one. (Skuld found that.)
/// </summary>
public sealed class OrderSummaries : Projection<OrderSummary>
{
    private static readonly string[] Lifecycle = ["Placed", "Reserved", "Paid", "Refunded", "Released", "Shipped", "Cancelled"];
    private static readonly string[] Final = ["Shipped", "Cancelled"];

    public OrderSummaries()
    {
        On<OrderPlaced>(e => e.OrderId, (view, e) => view ?? new OrderSummary(e.OrderId, e.Sku, e.Quantity, e.Amount, "Placed"));
        On<StockReserved>(e => e.OrderId, (view, _) => MoveTo(view, "Reserved"));
        On<PaymentCharged>(e => e.OrderId, (view, _) => MoveTo(view, "Paid"));
        On<PaymentRefunded>(e => e.OrderId, (view, _) => MoveTo(view, "Refunded"));
        On<StockReleased>(e => e.OrderId, (view, _) => MoveTo(view, "Released"));
        On<OrderShipped>(e => e.OrderId, (view, _) => MoveTo(view, "Shipped"));
        On<OrderCancelled>(e => e.OrderId, (view, _) => MoveTo(view, "Cancelled"));
    }

    private static OrderSummary? MoveTo(OrderSummary? view, string status) =>
        view is null || Final.Contains(view.Status) || Array.IndexOf(Lifecycle, status) < Array.IndexOf(Lifecycle, view.Status)
            ? view
            : view with { Status = status };
}
