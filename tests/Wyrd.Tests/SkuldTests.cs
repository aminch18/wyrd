using Wyrd.Processes;
using Wyrd.Runtime;
using Wyrd.Testing;

namespace Wyrd.Tests;

public class SkuldTests
{
    // No step retries: one transient failure stops an order that should have gone through.
    private static Process Fragile => Process
        .StartOn<OrderPlaced>(e => e.OrderId)
        .Then(Stock.Reserve).Undo(Stock.Release)
        .Then(Payment.Charge).Undo(Payment.Refund);

    // A-1 is a valid order: whatever the network does, it must go through.
    private static readonly Invariant ValidOrdersGoThrough = new("valid orders go through", runtime =>
        runtime.History.Contains(new PaymentCharged("A-1", 50)) && !runtime.History.Contains(new PaymentRefunded("A-1"))
            ? null
            : "A-1 didn't go through because of a transient failure");

    private static Process Robust => Process
        .StartOn<OrderPlaced>(e => e.OrderId)
        .Then(Stock.Reserve).Retry(5.Times).Undo(Stock.Release)
        .Then(Payment.Charge).Retry(5.Times).Undo(Payment.Refund);

    private static Func<InMemoryRuntime> System(Process process) => () => TestSystem.Create(process);

    private static async Task TwoOrders(InMemoryRuntime runtime)
    {
        await runtime.SendAsync(new PlaceOrder("A-1", Quantity: 1, Amount: 50));
        await runtime.SendAsync(new PlaceOrder("A-2", Quantity: 1, Amount: 5_000)); // declined: compensates
    }

    [Fact]
    public async Task skuld_finds_that_a_step_without_retries_stops_valid_orders()
    {
        var report = await Skuld.SimulateAsync(System(Fragile), TwoOrders, [.. Invariant.Defaults, ValidOrdersGoThrough]);

        var violation = report.Violations.First(v => v.Invariant == "valid orders go through");
        Assert.Contains(violation.Journal, e => e.Process == "skuld" && e.Message.EndsWith("failed transiently", StringComparison.Ordinal));
        Assert.Contains(violation.Journal, e => e.Message == "transient failure, no retry policy");

        // Wyrd doesn't guess: the instance is suspended for a human, not silently cancelled.
        Assert.Contains(report.Violations, v => v.Invariant == "nothing needs attention" && v.Detail.StartsWith("suspended:", StringComparison.Ordinal));
        Assert.DoesNotContain(report.Violations, v => v.Invariant == "processes settle");
    }

    [Fact]
    public async Task a_robust_process_survives_every_future() =>
        (await Skuld.SimulateAsync(System(Robust), TwoOrders, [.. Invariant.Defaults, ValidOrdersGoThrough])).ThrowIfViolated();

    [Fact]
    public async Task a_failing_future_is_replayed_exactly_from_its_seed()
    {
        var options = new SimulationOptions();
        var first = (await Skuld.SimulateAsync(System(Fragile), TwoOrders, [ValidOrdersGoThrough], options)).Violations[0];

        var replay = (await Skuld.SimulateAsync(System(Fragile), TwoOrders, [ValidOrdersGoThrough], options.Only(first.Seed))).Violations[0];

        Assert.Equal(first.Seed, replay.Seed);
        Assert.Equal(first.Invariant, replay.Invariant);
        Assert.Equal(first.Journal, replay.Journal);
    }

    [Fact]
    public async Task without_perturbations_skuld_is_a_plain_run() =>
        (await Skuld.SimulateAsync(
            System(Fragile),
            TwoOrders,
            Invariant.Defaults,
            new SimulationOptions { Runs = 20, FaultRate = 0, DuplicateRate = 0, ReorderRate = 0 })).ThrowIfViolated();

    [Fact]
    public async Task every_message_delivered_twice_still_terminates_and_holds() =>
        (await Skuld.SimulateAsync(
            System(Robust),
            TwoOrders,
            Invariant.Defaults,
            new SimulationOptions { Runs = 50, FaultRate = 0, DuplicateRate = 1, ReorderRate = 1 })).ThrowIfViolated();

    [Fact]
    public async Task the_report_explains_what_broke_and_how_to_replay_it()
    {
        var report = await Skuld.SimulateAsync(System(Fragile), TwoOrders, [ValidOrdersGoThrough]);

        var ex = Assert.Throws<WyrdAssertionException>(report.ThrowIfViolated);
        Assert.Contains("✗ \"valid orders go through\"", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Perturbations:", ex.Message, StringComparison.Ordinal);
        Assert.Contains($"options.Only({report.Violations[0].Seed})", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task custom_invariants_are_checked_at_the_end_of_every_future()
    {
        var neverShips = new Invariant("nothing ships", runtime =>
            runtime.History.OfType<OrderShipped>().Any() ? "something shipped" : null);
        Process withShipping = Process.StartOn<OrderPlaced>(e => e.OrderId).Then(Shipping.Ship).Retry(5.Times);

        var report = await Skuld.SimulateAsync(
            System(withShipping), TwoOrders, [neverShips], new SimulationOptions { Runs = 5 });

        Assert.Equal(5, report.Violations.Count);
        Assert.All(report.Violations, v => Assert.Equal("nothing ships", v.Invariant));
    }

    [Fact]
    public async Task a_scenario_that_throws_is_reported_as_a_violation()
    {
        var report = await Skuld.SimulateAsync(
            System(Robust),
            _ => throw new InvalidOperationException("boom"),
            Invariant.Defaults,
            new SimulationOptions { Runs = 3 });

        Assert.Equal(3, report.Violations.Count(v => v.Invariant == "runs without errors" && v.Detail.Contains("boom", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData(0, 0.1)]
    [InlineData(-1, 0.1)]
    [InlineData(10, -0.1)]
    [InlineData(10, 1.5)]
    [InlineData(10, double.NaN)]
    public async Task a_misconfigured_simulation_fails_instead_of_passing(int runs, double faultRate)
    {
        var alwaysBroken = new Invariant("always broken", _ => "broken");

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Skuld.SimulateAsync(
            System(Robust), TwoOrders, [alwaysBroken], new SimulationOptions { Runs = runs, FaultRate = faultRate }));
    }
}
