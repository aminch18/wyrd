using Wyrd;

namespace Shop.Inventory;

public sealed record ReserveStock(string Sku, int Quantity, string OrderId) : ICommand;

/// <summary>Slice: reserve units for an order.</summary>
public sealed class ReserveStockSlice : Slice<ReserveStock, StockItem, StockReserved, InventoryError>
{
    public override object KeyOf(ReserveStock command) => command.Sku;

    public override Result<StockReserved, InventoryError> Decide(ReserveStock command, StockItem stock) => stock switch
    {
        _ when stock.HasReserved(command.OrderId) => InventoryError.AlreadyReserved,
        _ when stock.Available < command.Quantity => InventoryError.OutOfStock,
        _ => new StockReserved(command.Sku, command.Quantity, command.OrderId),
    };
}
