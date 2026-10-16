using Wyrd;

namespace Shop.Inventory;

public sealed record Restock(string Sku, int Quantity) : ICommand;

/// <summary>Slice: the warehouse receives stock.</summary>
public sealed class RestockSlice : Slice<Restock, StockItem, StockAdded, InventoryError>
{
    public override object KeyOf(Restock command) => command.Sku;

    public override Result<StockAdded, InventoryError> Decide(Restock command, StockItem stock) =>
        new StockAdded(command.Sku, command.Quantity);
}
