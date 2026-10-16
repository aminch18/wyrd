using Wyrd;

namespace Shop.Inventory;

/// <summary>Slice: reserve units for an order.</summary>
public sealed record ReserveStock(string Sku, int Quantity, string OrderId)
    : ICommand<ReserveStock, StockItem, StockReserved, InventoryError>
{
    public object Key => Sku;

    public static Result<StockReserved, InventoryError> Decide(ReserveStock cmd, StockItem stock) =>
        stock.Available < cmd.Quantity
            ? InventoryError.OutOfStock
            : new StockReserved(cmd.Sku, cmd.Quantity, cmd.OrderId);
}
