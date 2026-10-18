using Wyrd;

namespace Shop.Orders;

public sealed record CancelOrder(string OrderId) : ICommand;

public sealed record OrderCancelled(string OrderId) : IDomainEvent;

/// <summary>Slice: the order is cancelled (today, when its fulfillment could not complete).</summary>
public sealed class CancelOrderSlice : Slice<CancelOrder, Order, OrderCancelled, OrderError>
{
    public override object KeyOf(CancelOrder command) => command.OrderId;

    public override Result<OrderCancelled, OrderError> Decide(CancelOrder command, Order order) => order switch
    {
        Order.Placed => new OrderCancelled(command.OrderId),
        Order.Cancelled => OrderError.AlreadyCancelled,
        Order.None => OrderError.NotFound,
    };
}
