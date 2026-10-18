using Wyrd;

namespace Shop.Orders;

public sealed record CancelLine(string OrderId, string LineId) : ICommand;

public sealed record LineCancelled(string OrderId, string LineId) : IDomainEvent;

public sealed class CancelLineSlice : Slice<CancelLine, Order, LineCancelled, OrderError>
{
    public override object KeyOf(CancelLine command) => command.OrderId;

    public override Result<LineCancelled, OrderError> Decide(CancelLine command, Order order) => order.CancelLine(command);
}
