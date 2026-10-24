using System.Collections.Immutable;
using Wyrd;

namespace Shop.Inventory;

public sealed record StockAdded(string Sku, int Quantity) : IDomainEvent;

public sealed record StockReserved(string Sku, int Quantity, string OrderId) : IDomainEvent;

public sealed record StockReleased(string Sku, int Quantity, string OrderId) : IDomainEvent;

public sealed record StockConsumed(string Sku, int Quantity, string OrderId) : IDomainEvent;

public enum InventoryError
{
    OutOfStock,
    AlreadyReserved,
    NotReserved,
}

/// <summary>
/// Stock for one SKU, and the orders still holding reservations. Shipping consumes a reservation without returning
/// units. Knowing the reservations makes releases safe to repeat or to send for an order that never reserved:
/// compensations must be harmless when there is nothing to undo.
/// </summary>
public sealed record StockItem(int Available, ImmutableDictionary<string, int> Reservations) : IState<StockItem>
{
    public static StockItem Initial { get; } = new(0, ImmutableDictionary<string, int>.Empty);

    public bool HasReserved(string orderId) => Reservations.ContainsKey(orderId);

    public StockItem Evolve(IDomainEvent domainEvent) => domainEvent switch
    {
        StockAdded added => this with { Available = Available + added.Quantity },
        StockReserved reserved => this with
        {
            Available = Available - reserved.Quantity,
            Reservations = Reservations.SetItem(reserved.OrderId, reserved.Quantity),
        },
        StockReleased released => this with
        {
            Available = Available + released.Quantity,
            Reservations = Reservations.Remove(released.OrderId),
        },
        StockConsumed consumed => this with { Reservations = Reservations.Remove(consumed.OrderId) },
        _ => this,
    };
}
