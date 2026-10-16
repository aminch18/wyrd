using Wyrd;

namespace Shop.Shipping;

public sealed record OrderShipped(string OrderId);

public enum ShippingError
{
    AlreadyShipped,
}

[Union]
public abstract record Shipment : IState<Shipment>
{
    public static Shipment Initial { get; } = new Pending();

    public Shipment Evolve(object domainEvent) => domainEvent is OrderShipped ? new Shipped() : this;

    public sealed record Pending : Shipment;

    public sealed record Shipped : Shipment;
}

/// <summary>Slice: hand the order to the carrier.</summary>
public sealed record ShipOrder(string OrderId) : ICommand<ShipOrder, Shipment, OrderShipped, ShippingError>
{
    public object Key => OrderId;

    public static Result<OrderShipped, ShippingError> Decide(ShipOrder cmd, Shipment shipment) => shipment switch
    {
        Shipment.Pending => new OrderShipped(cmd.OrderId),
        _ => ShippingError.AlreadyShipped,
    };
}
