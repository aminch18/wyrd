using System.Collections.Immutable;
using Wyrd;
using Wyrd.Runtime;

namespace Wyrd.Tests;

// A tiny order domain used across the tests.

// Commands: intentions, each with exactly one owner.
public sealed record PlaceOrder(string OrderId, int Quantity, decimal Amount) : ICommand;
public sealed record ReserveStock(string OrderId, int Quantity) : ICommand;
public sealed record ReleaseStock(string OrderId) : ICommand;
public sealed record ChargePayment(string OrderId, decimal Amount) : ICommand;
public sealed record RefundPayment(string OrderId) : ICommand;
public sealed record ShipOrder(string OrderId) : ICommand;
public sealed record CancelOrder(string OrderId) : ICommand;

// Domain events: internal facts.
public sealed record OrderPlaced(string OrderId, int Quantity, decimal Amount) : IDomainEvent;
public sealed record StockReserved(string OrderId) : IDomainEvent;
public sealed record StockReleased(string OrderId) : IDomainEvent;
public sealed record PaymentCharged(string OrderId, decimal Amount) : IDomainEvent;
public sealed record PaymentRefunded(string OrderId) : IDomainEvent;
public sealed record OrderShipped(string OrderId) : IDomainEvent;
public sealed record OrderCancelled(string OrderId) : IDomainEvent;

// Integration events: public, versioned contracts.
[IntegrationEvent("psp.payment-confirmed")]
public sealed record PaymentConfirmed(string OrderId) : IIntegrationEvent;

[IntegrationEvent("payments.payment-charged", Version = 1)]
public sealed record PaymentChargedV1(string OrderId, decimal Amount) : IIntegrationEvent;

[IntegrationEvent("payments.payment-charged", Version = 2)]
public sealed record PaymentChargedV2(string OrderId, decimal Amount, string Currency) : IIntegrationEvent;

[IntegrationEvent("payments.payment-charged", Version = 3)]
public sealed record PaymentChargedV3(string OrderId, decimal Amount, string Currency, string Channel) : IIntegrationEvent;

public sealed class PaymentChargedV1ToV2 : Upcaster<PaymentChargedV1, PaymentChargedV2>
{
    public override PaymentChargedV2 Map(PaymentChargedV1 previous) => new(previous.OrderId, previous.Amount, "EUR");
}

public sealed class PaymentChargedV2ToV3 : Upcaster<PaymentChargedV2, PaymentChargedV3>
{
    public override PaymentChargedV3 Map(PaymentChargedV2 previous) => new(previous.OrderId, previous.Amount, previous.Currency, "web");
}

public sealed class PaymentChargedV1ToV3 : Upcaster<PaymentChargedV1, PaymentChargedV3>
{
    public override PaymentChargedV3 Map(PaymentChargedV1 previous) => new(previous.OrderId, previous.Amount, "EUR", "web");
}

public enum StockError
{
    OutOfStock,
}

public enum PaymentError
{
    Declined,
    NothingToRefund,
}

// States are stored as JSON: they hold plain data (here, the names of what happened), never interfaces.
public sealed record EventLog(ImmutableList<string> Events) : IState<EventLog>
{
    public static EventLog Initial { get; } = new([]);

    public EventLog Evolve(IDomainEvent domainEvent) => new(Events.Add(domainEvent.GetType().Name));
}

public sealed class PlaceOrderSlice : Slice<PlaceOrder, EventLog, OrderPlaced, OrderError>
{
    public override object KeyOf(PlaceOrder command) => command.OrderId;

    public override Result<OrderPlaced, OrderError> Decide(PlaceOrder command, EventLog state) =>
        state.Events.IsEmpty ? new OrderPlaced(command.OrderId, command.Quantity, command.Amount) : OrderError.AlreadyExists;
}

public sealed class ReserveStockSlice : Slice<ReserveStock, EventLog, StockReserved, StockError>
{
    public override object KeyOf(ReserveStock command) => command.OrderId;

    public override Result<StockReserved, StockError> Decide(ReserveStock command, EventLog state) =>
        command.Quantity > 10 ? StockError.OutOfStock : new StockReserved(command.OrderId);
}

public sealed class ReleaseStockSlice : Slice<ReleaseStock, EventLog, StockReleased, StockError>
{
    public override object KeyOf(ReleaseStock command) => command.OrderId;

    public override Result<StockReleased, StockError> Decide(ReleaseStock command, EventLog state) =>
        new StockReleased(command.OrderId);
}

public sealed class ChargePaymentSlice : Slice<ChargePayment, EventLog, PaymentCharged, PaymentError>
{
    public override object KeyOf(ChargePayment command) => command.OrderId;

    public override Result<PaymentCharged, PaymentError> Decide(ChargePayment command, EventLog state) =>
        command.Amount > 1000 ? PaymentError.Declined : new PaymentCharged(command.OrderId, command.Amount);
}

public sealed class RefundPaymentSlice : Slice<RefundPayment, EventLog, PaymentRefunded, PaymentError>
{
    public override object KeyOf(RefundPayment command) => command.OrderId;

    public override Result<PaymentRefunded, PaymentError> Decide(RefundPayment command, EventLog state) =>
        state.Events.Contains(nameof(PaymentCharged)) ? new PaymentRefunded(command.OrderId) : PaymentError.NothingToRefund;
}

public sealed class ShipOrderSlice : Slice<ShipOrder, EventLog, OrderShipped, StockError>
{
    public override object KeyOf(ShipOrder command) => command.OrderId;

    public override Result<OrderShipped, StockError> Decide(ShipOrder command, EventLog state) =>
        new OrderShipped(command.OrderId);
}

public sealed class CancelOrderSlice : Slice<CancelOrder, EventLog, OrderCancelled, OrderError>
{
    public override object KeyOf(CancelOrder command) => command.OrderId;

    public override Result<OrderCancelled, OrderError> Decide(CancelOrder command, EventLog state) =>
        state.Events.Contains(nameof(OrderCancelled)) ? OrderError.AlreadyExists : new OrderCancelled(command.OrderId);
}

public sealed class PaymentChargedV1Publication : Publication<PaymentCharged, PaymentChargedV1>
{
    public override PaymentChargedV1 Map(PaymentCharged domainEvent) => new(domainEvent.OrderId, domainEvent.Amount);
}

public sealed class PaymentChargedV2Publication : Publication<PaymentCharged, PaymentChargedV2>
{
    public override PaymentChargedV2 Map(PaymentCharged domainEvent) => new(domainEvent.OrderId, domainEvent.Amount, "EUR");
}

// A read model for screens: one summary per order, kept up to date from events.
public sealed record OrderSummary(string OrderId, decimal Amount, string Status, int Charges = 0);

public sealed class OrderSummaries : Projection<OrderSummary>
{
    public OrderSummaries()
    {
        On<OrderPlaced>(e => e.OrderId, (_, e) => new OrderSummary(e.OrderId, e.Amount, "Placed"));
        On<PaymentCharged>(e => e.OrderId, (view, e) => view is null ? null : view with { Status = "Paid", Charges = view.Charges + 1 });
        On<OrderShipped>(e => e.OrderId, (view, e) => view! with { Status = "Shipped" });
        On<StockReleased>(e => e.OrderId, (view, e) => null); // a cancelled order disappears from the list
    }
}

public static class TestSystem
{
    public static InMemoryRuntime Create(params Wyrd.Processes.Process[] processes) =>
        Create(new InMemoryRuntime().AddSlice(new ChargePaymentSlice()), processes);

    /// <summary>The test system with the payment carried out by an integration instead of a slice.</summary>
    public static InMemoryRuntime WithGateway(Integration<ChargePayment, PaymentCharged, PaymentError> gateway, params Wyrd.Processes.Process[] processes) =>
        Create(new InMemoryRuntime().AddIntegration(gateway), processes);

    private static InMemoryRuntime Create(InMemoryRuntime payments, Wyrd.Processes.Process[] processes)
    {
        var runtime = payments
            .AddSlice(new PlaceOrderSlice())
            .AddSlice(new ReserveStockSlice())
            .AddSlice(new ReleaseStockSlice())
            .AddSlice(new RefundPaymentSlice())
            .AddSlice(new ShipOrderSlice())
            .AddSlice(new CancelOrderSlice())
            .AddPublication(new PaymentChargedV1Publication());

        foreach (var process in processes)
        {
            runtime.AddProcess(process);
        }

        return runtime;
    }
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

public static class Orders
{
    public static CancelOrder Cancel(OrderPlaced e) => new(e.OrderId);
}

public static class Shipping
{
    public static ShipOrder Ship(OrderPlaced e) => new(e.OrderId);

    public static ShipOrder ShipPaid(PaymentChargedV1 e) => new(e.OrderId);

    public static ShipOrder ShipPaidV2(PaymentChargedV2 e) => new($"{e.OrderId}/{e.Currency}");

    public static ShipOrder ShipPaidV3(PaymentChargedV3 e) => new($"{e.OrderId}/{e.Currency}/{e.Channel}");
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

public static class PlaceOrderDecider
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
