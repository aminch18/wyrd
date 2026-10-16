using Wyrd;

namespace Shop.Inventory;

/// <summary>Slice: give reserved units back.</summary>
public sealed record ReleaseStock(string Sku, int Quantity, string OrderId)
    : ICommand<ReleaseStock, StockItem, StockReleased, InventoryError>
{
    public object Key => Sku;

    public static Result<StockReleased, InventoryError> Decide(ReleaseStock cmd, StockItem stock) =>
        new StockReleased(cmd.Sku, cmd.Quantity, cmd.OrderId);
}
