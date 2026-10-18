using System.Text.Json.Serialization;
using Shop.Contracts;
using Wyrd;

namespace Shop.Shipping;

public sealed record ShipOrder(string OrderId) : ICommand;

public sealed record OrderShipped(string OrderId) : IDomainEvent;

public enum ShippingError
{
    AlreadyShipped,
}

[Union]
[JsonDerivedType(typeof(Pending), "Pending")]
[JsonDerivedType(typeof(Shipped), "Shipped")]
public abstract record Shipment : IState<Shipment>
{
    public static Shipment Initial { get; } = new Pending();

    public Shipment Evolve(IDomainEvent domainEvent) => domainEvent is OrderShipped ? new Shipped() : this;

    public sealed record Pending : Shipment;

    public sealed record Shipped : Shipment;
}

/// <summary>Slice: hand the order to the carrier.</summary>
public sealed class ShipOrderSlice : Slice<ShipOrder, Shipment, OrderShipped, ShippingError>
{
    public override object KeyOf(ShipOrder command) => command.OrderId;

    public override Result<OrderShipped, ShippingError> Decide(ShipOrder command, Shipment shipment) => shipment switch
    {
        Shipment.Pending => new OrderShipped(command.OrderId),
        Shipment.Shipped => ShippingError.AlreadyShipped,
    };
}

public sealed class OrderShippedPublication : Publication<OrderShipped, OrderShippedV1>
{
    public override OrderShippedV1 Map(OrderShipped domainEvent) => new(domainEvent.OrderId, new DateOnly(2026, 10, 16));
}
