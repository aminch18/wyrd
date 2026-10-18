using Shop.Contracts;
using Shop.Inventory;
using Shop.Orders;
using Wyrd.Runtime;
using Wyrd.Testing;

namespace Shop.Tests;

/// <summary>Skuld explores hostile futures of a busy afternoon at the shop and checks the business still holds.</summary>
public class SkuldSpecs
{
    private const int Received = 5;

    // A suspended order keeps its reservation on purpose, until someone resumes or compensates it.
    private static readonly Invariant StockIsConserved = new("stock is conserved", shop =>
    {
        var shipped = shop.Views<OrderSummary>().Where(o => o.Status == "Shipped").Sum(o => o.Quantity);
        var stock = shop.StateOf<StockItem>("KEYBOARD");
        var heldBySuspended = stock.Reservations
            .Where(r => shop.StatusOf(Shop.Fulfillment.Processes.OrderFulfillment, r.Key) == ProcessStatus.Suspended)
            .Sum(r => r.Value);
        var expected = Received - shipped - heldBySuspended;
        return stock.Available == expected
            ? null
            : $"{stock.Available} keyboards available, expected {Received} received - {shipped} shipped - {heldBySuspended} held by suspended orders";
    });

    private static readonly Invariant EveryOrderEnds = new("every order ends shipped or cancelled", shop =>
    {
        var open = shop.Views<OrderSummary>().Where(o => o.Status is not ("Shipped" or "Cancelled")).ToList();
        return open.Count == 0 ? null : $"orders left open: {string.Join(", ", open.Select(o => $"{o.OrderId} ({o.Status})"))}";
    });

    private static readonly Invariant EveryOrderEndsOrIsFlagged = new("every order ends, or is flagged for a human", shop =>
    {
        var open = shop.Views<OrderSummary>()
            .Where(o => o.Status is not ("Shipped" or "Cancelled"))
            .Where(o => shop.StatusOf(Shop.Fulfillment.Processes.OrderFulfillment, o.OrderId) != ProcessStatus.Suspended)
            .ToList();
        return open.Count == 0 ? null : $"orders left open and unflagged: {string.Join(", ", open.Select(o => $"{o.OrderId} ({o.Status})"))}";
    });

    private static async Task BusyAfternoon(InMemoryRuntime shop)
    {
        await shop.SendAsync(new Restock("KEYBOARD", Received));
        await shop.SendAsync(new PlaceOrder("A-1", "KEYBOARD", 1, 89m));      // will be confirmed
        await shop.SendAsync(new PlaceOrder("A-2", "KEYBOARD", 2, 2_500m));   // card declined
        await shop.SendAsync(new PlaceOrder("A-3", "KEYBOARD", 1, 89m));      // never confirmed: times out
        await shop.PublishAsync(new PaymentConfirmed("A-1"));
    }

    [Fact]
    public async Task the_business_holds_in_every_future()
    {
        var report = await Skuld.SimulateAsync(
            ShopModule.CreateRuntime,
            BusyAfternoon,
            [.. Invariant.Defaults, StockIsConserved, EveryOrderEnds]);

        report.ThrowIfViolated();
    }

    /// <summary>
    /// Much more hostile futures. With 20% faults a command can fail six times in a row, so an order may end up
    /// flagged for a human — that is the correct outcome. What must hold in every single future: stock is never
    /// lost, no process is left half-way, and nothing is left open silently.
    /// </summary>
    [Fact]
    public async Task nothing_is_lost_in_thousands_of_hostile_futures()
    {
        var report = await Skuld.SimulateAsync(
            ShopModule.CreateRuntime,
            BusyAfternoon,
            [Invariant.ProcessesSettle, StockIsConserved, EveryOrderEndsOrIsFlagged],
            new SimulationOptions { Runs = 5_000, FaultRate = 0.2, DuplicateRate = 0.2, ReorderRate = 0.5 });

        report.ThrowIfViolated();
    }
}
