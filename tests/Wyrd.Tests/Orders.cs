using System.Collections.Immutable;
using Wyrd;

namespace Wyrd.Tests;

// A tiny order domain used across the tests. Every slice keeps its own log keyed by order id.

public sealed record OrderPlaced(string OrderId, int Quantity, decimal Amount);
public sealed record PaymentConfirmed(string OrderId);

public sealed record StockReserved(string OrderId);
public sealed record StockReleased(string OrderId);
public sealed record PaymentCharged(string OrderId);
public sealed record PaymentRefunded(string OrderId);
public sealed record OrderShipped(string OrderId);

public enum StockError
{
    OutOfStock,
}

public enum PaymentError
{
    Declined,
}

public sealed record EventLog(ImmutableList<object> Events) : IState<EventLog>
{
    public static EventLog Initial { get; } = new([]);

    public EventLog Evolve(object domainEvent) => new(Events.Add(domainEvent));
}

public sealed record ReserveStock(string OrderId, int Quantity) : ICommand<ReserveStock, EventLog, StockReserved, StockError>
{
    public object Key => OrderId;

    public static Result<StockReserved, StockError> Decide(ReserveStock cmd, EventLog state) =>
        cmd.Quantity > 10 ? StockError.OutOfStock : new StockReserved(cmd.OrderId);
}

public sealed record ReleaseStock(string OrderId) : ICommand<ReleaseStock, EventLog, StockReleased, StockError>
{
    public object Key => OrderId;

    public static Result<StockReleased, StockError> Decide(ReleaseStock cmd, EventLog state) => new StockReleased(cmd.OrderId);
}

public sealed record ChargePayment(string OrderId, decimal Amount) : ICommand<ChargePayment, EventLog, PaymentCharged, PaymentError>
{
    public object Key => OrderId;

    public static Result<PaymentCharged, PaymentError> Decide(ChargePayment cmd, EventLog state) =>
        cmd.Amount > 1000 ? PaymentError.Declined : new PaymentCharged(cmd.OrderId);
}

public sealed record RefundPayment(string OrderId) : ICommand<RefundPayment, EventLog, PaymentRefunded, PaymentError>
{
    public object Key => OrderId;

    public static Result<PaymentRefunded, PaymentError> Decide(RefundPayment cmd, EventLog state) => new PaymentRefunded(cmd.OrderId);
}

public sealed record ShipOrder(string OrderId) : ICommand<ShipOrder, EventLog, OrderShipped, StockError>
{
    public object Key => OrderId;

    public static Result<OrderShipped, StockError> Decide(ShipOrder cmd, EventLog state) => new OrderShipped(cmd.OrderId);
}

// Steps: pure functions from the process trigger to a command.

public static class Stock
{
    public static ReserveStock Reserve(OrderPlaced e) => new(e.OrderId, e.Quantity);

    public static ReleaseStock Release(OrderPlaced e) => new(e.OrderId);
}

public static class Payment
{
    public static ChargePayment Charge(OrderPlaced e) => new(e.OrderId, e.Amount);

    public static RefundPayment Refund(OrderPlaced e) => new(e.OrderId);
}

public static class Shipping
{
    public static ShipOrder Ship(OrderPlaced e) => new(e.OrderId);
}

// A pure decider with a closed state, used by DeciderTests.

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
            Order.None => new OrderPlaced(cmd.Id.ToString(), cmd.Lines, 0),
            _ => OrderError.AlreadyExists,
        };
}
