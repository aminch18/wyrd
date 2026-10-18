using Shop.Inventory;
using Shop.Orders;
using Wyrd.Processes;
using Wyrd.Runtime;
using Wyrd.Testing;

namespace Shop.Tests;

/// <summary>
/// Regression for the PR #7 review: a process that gives up on ReserveStock without knowing whether it ran must never
/// report a completed compensation while a delayed copy of ReserveStock still takes the stock.
/// </summary>
public class FencingSpecs
{
    private static readonly Process ReserveOnly = Process.StartOn<OrderPlaced>(e => e.OrderId, "ReserveOnly")
        .Then(e => new ReserveStock(e.Sku, e.Quantity, e.OrderId))
        .Undo(e => new ReleaseStock(e.Sku, e.Quantity, e.OrderId));

    private static readonly SimulationOptions Hostile = new()
    {
        Runs = 1_000, Seed = 1, FaultRate = .5, DuplicateRate = 1, ReorderRate = 1,
    };

    private static InMemoryRuntime Create() => new InMemoryRuntime(ShopJson.Default)
        .AddSlice(new RestockSlice()).AddSlice(new PlaceOrderSlice())
        .AddSlice(new ReserveStockSlice()).AddSlice(new ReleaseStockSlice())
        .AddProcess(ReserveOnly);

    private static async Task OneOrder(InMemoryRuntime runtime)
    {
        await runtime.SendAsync(new Restock("SKU", 5));
        await runtime.SendAsync(new PlaceOrder("A", "SKU", 1, 10));
    }

    private static readonly Invariant CompensatedRestoresStock = new("compensated stock is restored", runtime =>
        runtime.StatusOf(ReserveOnly, "A") == ProcessStatus.Compensated && runtime.StateOf<StockItem>("SKU").Available != 5
            ? "Compensated, but stock is still reserved"
            : null);

    [Fact]
    public async Task the_reviewers_seed_holds() =>
        (await Skuld.SimulateAsync(Create, OneOrder, [CompensatedRestoresStock], Hostile.Only(9))).ThrowIfViolated();

    [Fact]
    public async Task no_future_ends_compensated_with_the_stock_still_reserved() =>
        (await Skuld.SimulateAsync(Create, OneOrder, [CompensatedRestoresStock, Invariant.ProcessesSettle], Hostile)).ThrowIfViolated();

    [Fact]
    public async Task late_copies_of_a_fenced_command_never_execute()
    {
        var fencedLateCopies = 0;
        var observe = new Invariant("observe fencing", runtime =>
        {
            fencedLateCopies += runtime.Journal.Count(e => e.Message == "late ReserveStock arrived after being fenced: never executed");
            return null;
        });

        (await Skuld.SimulateAsync(Create, OneOrder, [observe, CompensatedRestoresStock], Hostile)).ThrowIfViolated();

        Assert.True(fencedLateCopies > 0, "The hostile futures never exercised a late copy of a fenced command.");
    }
}
