using Wyrd;

namespace Shop.Orders;

public sealed record OrderPlaced(string OrderId, string Sku, int Quantity, decimal Amount);

public enum OrderError
{
    EmptyOrder,
    AlreadyPlaced,
}

[Union]
public abstract record Order : IState<Order>
{
    public static Order Initial { get; } = new None();

    public Order Evolve(object domainEvent) => (this, domainEvent) switch
    {
        (None, OrderPlaced placed) => new Placed(placed.Sku, placed.Quantity),
        _ => this,
    };

    public sealed record None : Order;

    public sealed record Placed(string Sku, int Quantity) : Order;
}

/// <summary>Slice: a customer places an order.</summary>
public sealed record PlaceOrder(string OrderId, string Sku, int Quantity, decimal Amount)
    : ICommand<PlaceOrder, Order, OrderPlaced, OrderError>
{
    public object Key => OrderId;

    public static Result<OrderPlaced, OrderError> Decide(PlaceOrder cmd, Order order) => order switch
    {
        Order.None when cmd.Quantity <= 0 => OrderError.EmptyOrder,
        Order.None => new OrderPlaced(cmd.OrderId, cmd.Sku, cmd.Quantity, cmd.Amount),
        _ => OrderError.AlreadyPlaced,
    };
}
