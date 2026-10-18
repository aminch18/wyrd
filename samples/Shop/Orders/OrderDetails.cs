using System.Collections.Immutable;
using Wyrd;

namespace Shop.Orders;

/// <summary>The order's lines and total, including retained cancelled line identities.</summary>
public sealed record OrderDetail(string OrderId, ImmutableList<OrderLine> Lines, Money? Total, string Status);

/// <summary>Projects facts from the order stream in their original order.</summary>
public sealed class OrderDetails : Projection<OrderDetail>
{
    public OrderDetails()
    {
        On<LineAdded>(e => e.OrderId, (view, e) =>
            Draft(e.OrderId, (view?.Lines ?? []).Add(e.Line)));
        On<LineCancelled>(e => e.OrderId, (view, e) => view is null
            ? null
            : Draft(e.OrderId, view.Lines.Select(line => line.LineId == e.LineId ? line.Cancel() : line).ToImmutableList()));
        On<OrderPlaced>(e => e.OrderId, (_, e) =>
        {
            var lines = Order.LinesOf(e);
            return new OrderDetail(e.OrderId, lines, Order.TotalOf(lines), "Placed");
        });
        On<OrderCancelled>(e => e.OrderId, (view, _) => view is null ? null : view with { Status = "Cancelled" });
    }

    private static OrderDetail Draft(string orderId, ImmutableList<OrderLine> lines) =>
        new(orderId, lines, Order.TotalOf(lines), "Draft");
}
