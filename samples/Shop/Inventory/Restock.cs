using Wyrd;

namespace Shop.Inventory;

/// <summary>Slice: the warehouse receives stock.</summary>
public sealed record Restock(string Sku, int Quantity) : ICommand<Restock, StockItem, StockAdded, InventoryError>
{
    public object Key => Sku;

    public static Result<StockAdded, InventoryError> Decide(Restock cmd, StockItem stock) =>
        new StockAdded(cmd.Sku, cmd.Quantity);
}
