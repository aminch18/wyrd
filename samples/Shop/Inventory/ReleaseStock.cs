using Wyrd;

namespace Shop.Inventory;

public sealed record ReleaseStock(string Sku, int Quantity, string OrderId) : ICommand;

/// <summary>Slice: give an order's reserved units back. Only what was actually reserved.</summary>
public sealed class ReleaseStockSlice : Slice<ReleaseStock, StockItem, StockReleased, InventoryError>
{
    public override object KeyOf(ReleaseStock command) => command.Sku;

    public override Result<StockReleased, InventoryError> Decide(ReleaseStock command, StockItem stock) =>
        stock.Reservations.TryGetValue(command.OrderId, out var reserved)
            ? new StockReleased(command.Sku, reserved, command.OrderId)
            : InventoryError.NotReserved;
}
