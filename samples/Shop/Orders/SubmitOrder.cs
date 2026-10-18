using Wyrd;

namespace Shop.Orders;

public sealed record SubmitOrder(string OrderId) : ICommand;

public sealed class SubmitOrderSlice : Slice<SubmitOrder, Order, OrderPlaced, OrderError>
{
    public override object KeyOf(SubmitOrder command) => command.OrderId;

    public override Result<OrderPlaced, OrderError> Decide(SubmitOrder command, Order order) => order.Submit(command);
}
