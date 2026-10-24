using Wyrd;

namespace Shop.Inventory;

public sealed record ConsumeStock(string Sku, string OrderId) : ICommand;

/// <summary>Forget a shipped order's reservation without returning its units to available stock.</summary>
public sealed class ConsumeStockSlice : Slice<ConsumeStock, StockItem, StockConsumed, InventoryError>
{
    public override object KeyOf(ConsumeStock command) => command.Sku;

    public override Result<StockConsumed, InventoryError> Decide(ConsumeStock command, StockItem stock) =>
        stock.Reservations.TryGetValue(command.OrderId, out var reserved)
            ? new StockConsumed(command.Sku, reserved, command.OrderId)
            : InventoryError.NotReserved;
}
