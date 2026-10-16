using Wyrd;

namespace Wyrd.Tests;

// A tiny sample domain used across the tests.
public sealed record OrderPlaced(Guid OrderId);
public sealed record PaymentConfirmed(Guid OrderId);
public sealed record ReserveStock(Guid OrderId);
public sealed record ReleaseStock(Guid OrderId);
public sealed record ChargePayment(Guid OrderId);
public sealed record RefundPayment(Guid OrderId);
public sealed record ShipOrder(Guid OrderId);

public static class Stock
{
    public static ReserveStock Reserve(OrderPlaced e) => new(e.OrderId);

    public static ReleaseStock Release(OrderPlaced e) => new(e.OrderId);
}

public static class Payment
{
    public static ChargePayment Charge(OrderPlaced e) => new(e.OrderId);

    public static RefundPayment Refund(OrderPlaced e) => new(e.OrderId);
}

public static class Shipping
{
    public static ShipOrder Ship(OrderPlaced e) => new(e.OrderId);
}

[Union]
public abstract partial record Order
{
    public sealed record None : Order;

    public sealed record Placed(Guid Id, int Lines) : Order;
}

public enum OrderError
{
    EmptyOrder,
    AlreadyExists,
}

public static class PlaceOrder
{
    public sealed record Command(Guid Id, int Lines);

    public static Result<OrderPlaced, OrderError> Decide(Command cmd, Order state) =>
        state switch
        {
            Order.None when cmd.Lines == 0 => OrderError.EmptyOrder,
            Order.None => new OrderPlaced(cmd.Id),
            _ => OrderError.AlreadyExists,
        };
}
