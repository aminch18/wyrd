using System.Collections.Immutable;
using Wyrd;

namespace Shop.Orders;

public sealed record PlaceOrder(string OrderId, string Sku, int Quantity, decimal Amount) : ICommand;

// Internal fact: optional rich lines preserve the complete draft without changing public integration contracts.
public sealed record OrderPlaced(
    string OrderId, string Sku, int Quantity, decimal Amount, ImmutableList<OrderLine>? Lines = null) : IDomainEvent;

/// <summary>Slice: a customer places a one-line order directly.</summary>
public sealed class PlaceOrderSlice : Slice<PlaceOrder, Order, OrderPlaced, OrderError>
{
    public override object KeyOf(PlaceOrder command) => command.OrderId;

    public override Result<OrderPlaced, OrderError> Decide(PlaceOrder command, Order order) => order.Place(command);
}
