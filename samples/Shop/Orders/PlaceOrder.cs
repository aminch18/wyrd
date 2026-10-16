using System.Text.Json.Serialization;
using Wyrd;

namespace Shop.Orders;

public sealed record PlaceOrder(string OrderId, string Sku, int Quantity, decimal Amount) : ICommand;

public sealed record OrderPlaced(string OrderId, string Sku, int Quantity, decimal Amount) : IDomainEvent;

public enum OrderError
{
    EmptyOrder,
    AlreadyPlaced,
    AlreadyCancelled,
    NotFound,
}

[Union]
[JsonDerivedType(typeof(None), "None")]
[JsonDerivedType(typeof(Placed), "Placed")]
[JsonDerivedType(typeof(Cancelled), "Cancelled")]
public abstract record Order : IState<Order>
{
    public static Order Initial { get; } = new None();

    public Order Evolve(IDomainEvent domainEvent) => (this, domainEvent) switch
    {
        (None, OrderPlaced placed) => new Placed(placed.Sku, placed.Quantity),
        (Placed, OrderCancelled) => new Cancelled(),
        _ => this,
    };

    public sealed record None : Order;

    public sealed record Placed(string Sku, int Quantity) : Order;

    public sealed record Cancelled : Order;
}

/// <summary>Slice: a customer places an order.</summary>
public sealed class PlaceOrderSlice : Slice<PlaceOrder, Order, OrderPlaced, OrderError>
{
    public override object KeyOf(PlaceOrder command) => command.OrderId;

    public override Result<OrderPlaced, OrderError> Decide(PlaceOrder command, Order order) => order switch
    {
        Order.None when command.Quantity <= 0 => OrderError.EmptyOrder,
        Order.None => new OrderPlaced(command.OrderId, command.Sku, command.Quantity, command.Amount),
        _ => OrderError.AlreadyPlaced,
    };
}
