using System.Collections.Immutable;
using Shop.Inventory;
using Wyrd.Testing;

namespace Shop.Tests;

public class InventorySpecs
{
    [Fact]
    public void units_can_be_reserved_while_there_is_stock() =>
        Scenario.For(new ReserveStockSlice())
            .Given(new StockAdded("KEYBOARD", 5))
            .When(new ReserveStock("KEYBOARD", 2, "A-1"))
            .Then(new StockReserved("KEYBOARD", 2, "A-1"))
            .ThenState(new StockItem(3, ImmutableDictionary<string, int>.Empty.Add("A-1", 2)));

    [Fact]
    public void reserving_more_than_available_is_rejected() =>
        Scenario.For(new ReserveStockSlice())
            .Given(new StockAdded("KEYBOARD", 5), new StockReserved("KEYBOARD", 4, "A-1"))
            .When(new ReserveStock("KEYBOARD", 2, "A-2"))
            .ThenRejected(InventoryError.OutOfStock);

    [Fact]
    public void released_units_are_available_again() =>
        Scenario.For(new ReleaseStockSlice())
            .Given(new StockAdded("KEYBOARD", 5), new StockReserved("KEYBOARD", 2, "A-1"))
            .When(new ReleaseStock("KEYBOARD", 2, "A-1"))
            .ThenState(new StockItem(5, ImmutableDictionary<string, int>.Empty));

    [Fact]
    public void an_order_that_never_reserved_releases_nothing() =>
        Scenario.For(new ReleaseStockSlice())
            .Given(new StockAdded("KEYBOARD", 5))
            .When(new ReleaseStock("KEYBOARD", 2, "A-1"))
            .ThenRejected(InventoryError.NotReserved);

    [Fact]
    public void an_order_reserves_once() =>
        Scenario.For(new ReserveStockSlice())
            .Given(new StockAdded("KEYBOARD", 5), new StockReserved("KEYBOARD", 1, "A-1"))
            .When(new ReserveStock("KEYBOARD", 1, "A-1"))
            .ThenRejected(InventoryError.AlreadyReserved);
}
