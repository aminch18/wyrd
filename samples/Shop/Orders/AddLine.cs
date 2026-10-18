using Wyrd;

namespace Shop.Orders;

/// <summary>Amount is the whole line's price, not a per-unit price. The first line starts the draft.</summary>
public sealed record AddLine(
    string OrderId, string LineId, string Sku, int Quantity, decimal Amount, string Currency = "EUR") : ICommand;

public sealed record LineAdded(string OrderId, OrderLine Line) : IDomainEvent;

public sealed class AddLineSlice : Slice<AddLine, Order, LineAdded, OrderError>
{
    public override object KeyOf(AddLine command) => command.OrderId;

    public override Result<LineAdded, OrderError> Decide(AddLine command, Order order) => order.AddLine(command);
}
