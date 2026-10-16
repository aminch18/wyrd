using Wyrd;

namespace Shop.Inventory;

public sealed record StockAdded(string Sku, int Quantity) : IDomainEvent;

public sealed record StockReserved(string Sku, int Quantity, string OrderId) : IDomainEvent;

public sealed record StockReleased(string Sku, int Quantity, string OrderId) : IDomainEvent;

public enum InventoryError
{
    OutOfStock,
}

/// <summary>Stock for one SKU.</summary>
public sealed record StockItem(int Available) : IState<StockItem>
{
    public static StockItem Initial { get; } = new(0);

    public StockItem Evolve(IDomainEvent domainEvent) => domainEvent switch
    {
        StockAdded added => this with { Available = Available + added.Quantity },
        StockReserved reserved => this with { Available = Available - reserved.Quantity },
        StockReleased released => this with { Available = Available + released.Quantity },
        _ => this,
    };
}
