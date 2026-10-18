using Wyrd;

namespace Shop.Orders;

/// <summary>User intent, distinct from the fulfillment process's cancellation after compensation.</summary>
public sealed record CancelDraftOrder(string OrderId) : ICommand;

public sealed class CancelDraftOrderSlice : Slice<CancelDraftOrder, Order, OrderCancelled, OrderError>
{
    public override object KeyOf(CancelDraftOrder command) => command.OrderId;

    public override Result<OrderCancelled, OrderError> Decide(CancelDraftOrder command, Order order) => order.CancelDraft(command);
}
