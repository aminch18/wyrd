using Wyrd;

namespace Shop.Inventory;

public sealed record ReleaseStock(string Sku, int Quantity, string OrderId) : ICommand;

/// <summary>Slice: give reserved units back.</summary>
public sealed class ReleaseStockSlice : Slice<ReleaseStock, StockItem, StockReleased, InventoryError>
{
    public override object KeyOf(ReleaseStock command) => command.Sku;

    public override Result<StockReleased, InventoryError> Decide(ReleaseStock command, StockItem stock) =>
        new StockReleased(command.Sku, command.Quantity, command.OrderId);
}
