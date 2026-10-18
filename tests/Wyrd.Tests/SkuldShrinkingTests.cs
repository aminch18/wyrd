using Wyrd.Processes;
using Wyrd.Runtime;
using Wyrd.Testing;

namespace Wyrd.Tests;

public class SkuldShrinkingTests
{
    private static Process Fragile => Process.StartOn<OrderPlaced>(e => e.OrderId)
        .Then(Stock.Reserve).Undo(Stock.Release)
        .Then(Payment.Charge).Undo(Payment.Refund);

    private static readonly Invariant ValidOrdersGoThrough = new("valid orders go through", runtime =>
        runtime.History.Contains(new PaymentCharged("A-1", 50))
        && !runtime.History.Contains(new PaymentRefunded("A-1")) ? null : "A-1 didn't go through");

    private static async Task TwoOrders(InMemoryRuntime runtime)
    {
        await runtime.SendAsync(new PlaceOrder("A-1", Quantity: 1, Amount: 50));
        await runtime.SendAsync(new PlaceOrder("A-2", Quantity: 1, Amount: 5_000));
    }

    private static Envelope Command(int id) => new(new Guid(id, 0, 0, new byte[8]), MessageKind.Command, MessageTypes.NameOf(typeof(ReserveStock)), "{}");

    [Fact]
    public void records_every_hook_including_neutral_answers()
    {
        using var runtime = TestSystem.Create();
        var decisions = new SimulationDecisions(runtime, new Random(1), new SimulationOptions { DuplicateRate = 1, FaultRate = 1, ReorderRate = 1 });
        var first = Command(1);
        var second = Command(2);

        Assert.Equal(0, decisions.PickNext([first]));
        Assert.Equal(1, decisions.PickNext([first, second]));
        Assert.True(decisions.Duplicate(first));
        Assert.False(decisions.Duplicate(first));
        Assert.True(decisions.Fault(first, typeof(ReserveStock)));

        Assert.Equal(new[]
        {
            new SimulationDecision(SimulationHook.PickNext, 0),
            new SimulationDecision(SimulationHook.PickNext, 1),
            new SimulationDecision(SimulationHook.Duplicate, 1),
            new SimulationDecision(SimulationHook.Duplicate, 0),
            new SimulationDecision(SimulationHook.Fault, 1),
        }, decisions.Recorded);
    }

    [Fact]
    public void replay_consumes_one_entry_per_hook_and_defaults_to_neutral()
    {
        using var runtime = TestSystem.Create();
        var replay = new SimulationDecisions(runtime, new Random(1), new SimulationOptions { FaultRate = 1, DuplicateRate = 1 },
        [new(SimulationHook.PickNext, 0), new(SimulationHook.Duplicate, 0), new(SimulationHook.Fault, 0)]);
        var command = Command(1);

        Assert.Equal(0, replay.PickNext([command, Command(2)]));
        Assert.False(replay.Duplicate(command));
        Assert.False(replay.Fault(command, typeof(ReserveStock)));
        Assert.False(replay.Fault(command, typeof(ReserveStock)));
        Assert.Empty(runtime.Journal);
    }

    [Fact]
    public void shifted_or_out_of_range_decisions_are_neutral_and_duplicates_remain_bounded()
    {
        using var runtime = TestSystem.Create();
        var replay = new SimulationDecisions(runtime, new Random(1), new SimulationOptions(),
        [new(SimulationHook.PickNext, 3), new(SimulationHook.Fault, 1),
            new(SimulationHook.Duplicate, 1), new(SimulationHook.Duplicate, 1)]);
        var command = Command(1);

        Assert.Equal(0, replay.PickNext([command]));
        Assert.False(replay.Duplicate(command)); // A different hook now occupies this position.
        Assert.True(replay.Duplicate(command));
        Assert.False(replay.Duplicate(command));
    }

    [Fact]
    public async Task replaying_the_recorded_stream_reproduces_the_original_journal()
    {
        var options = new SimulationOptions { FaultRate = 0.3, DuplicateRate = 0.5, ReorderRate = 0.7 };
        using var original = TestSystem.Create(Fragile);
        var decisions = new SimulationDecisions(original, new Random(12), options);
        original.Simulation = decisions;
        await TwoOrders(original);
        await original.AdvanceAsync(options.SettleFor);

        using var replay = TestSystem.Create(Fragile);
        replay.Simulation = new SimulationDecisions(replay, new Random(999), options, decisions.Recorded);
        await TwoOrders(replay);
        await replay.AdvanceAsync(options.SettleFor);

        Assert.Equal(original.Journal, replay.Journal);
        Assert.Equal(original.History, replay.History);
    }

    [Fact]
    public async Task fragile_future_shrinks_to_one_transient_failure_deterministically()
    {
        var options = new SimulationOptions { Runs = 20 };
        var report = await Skuld.SimulateAsync(() => TestSystem.Create(Fragile), TwoOrders, [ValidOrdersGoThrough], options);
        var violation = Assert.Single(report.Violations.Take(1));
        var shrunk = Assert.IsType<ShrunkFuture>(violation.Shrunk);
        Assert.EndsWith("failed transiently", Assert.Single(shrunk.Perturbations), StringComparison.Ordinal);
        Assert.Contains(shrunk.Journal, entry => entry.Message == "transient failure, no retry policy");
        Assert.True(shrunk.Perturbations.Count < violation.Journal.Count(entry => entry.Process == "skuld"));

        var replay = await Skuld.SimulateAsync(() => TestSystem.Create(Fragile), TwoOrders, [ValidOrdersGoThrough], options.Only(violation.Seed));
        var repeated = Assert.IsType<ShrunkFuture>(Assert.Single(replay.Violations).Shrunk);
        Assert.Equal(shrunk.Perturbations, repeated.Perturbations);
        Assert.Equal(shrunk.Journal, repeated.Journal);
        Assert.Equal(shrunk.Replays, repeated.Replays);
        Assert.Equal(violation.Journal, replay.Violations[0].Journal);
    }

    [Fact]
    public async Task greedy_removal_revisits_individuals_and_tries_pairs()
    {
        SimulationDecision[] stream = [new(SimulationHook.Fault, 1), new(SimulationHook.Fault, 1), new(SimulationHook.Fault, 1)];
        var original = new ShrunkFuture("broken", ["0", "1", "2"], [], 0);
        var shrunk = await FutureShrinker.ShrinkAsync(stream, original, candidate =>
        {
            // Removing either of the first two alone repairs it; removing both leaves the causal third fault.
            var breaks = candidate[2].Answer == 1 && candidate[0].Answer == candidate[1].Answer;
            var perturbations = candidate.Select((decision, index) => (decision, index))
                .Where(item => item.decision.Answer != 0).Select(item => item.index.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            return Task.FromResult(breaks ? new ShrunkFuture("broken", perturbations, [], 0) : null);
        }, 200);

        Assert.Equal("2", Assert.Single(shrunk.Perturbations));
        Assert.InRange(shrunk.Replays, 1, 200);
    }

    [Fact]
    public async Task passing_simulation_and_disabled_shrinking_do_no_replays()
    {
        var calls = 0;
        Task Scenario(InMemoryRuntime runtime) { calls++; return TwoOrders(runtime); }
        var options = new SimulationOptions { Runs = 3, FaultRate = 0, DuplicateRate = 0, ReorderRate = 0 };
        var passing = await Skuld.SimulateAsync(() => TestSystem.Create(Fragile), Scenario, [ValidOrdersGoThrough], options);
        Assert.True(passing.Passed);
        Assert.Equal(3, calls);

        calls = 0;
        var broken = await Skuld.SimulateAsync(() => TestSystem.Create(Fragile), Scenario,
            [new("broken", _ => "always")], options with { Shrink = false });
        Assert.Equal(3, calls);
        Assert.All(broken.Violations, violation => Assert.Null(violation.Shrunk));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public async Task replay_budget_is_shared_by_all_invariants_of_a_future(int budget)
    {
        var calls = 0;
        Task Scenario(InMemoryRuntime runtime) { calls++; return TwoOrders(runtime); }
        var broken = await Skuld.SimulateAsync(() => TestSystem.Create(Fragile), Scenario,
            [ValidOrdersGoThrough, new("same failure", ValidOrdersGoThrough.Check)],
            new SimulationOptions { Runs = 1, FaultRate = 1, DuplicateRate = 1, ShrinkBudget = budget });

        Assert.Equal(2, broken.Violations.Count);
        Assert.InRange(calls, 1, 1 + budget);
        Assert.Equal(calls - 1, broken.Violations.Sum(violation => violation.Shrunk!.Replays));
    }

    [Fact]
    public async Task an_unperturbed_failure_shrinks_to_zero_and_reports_its_own_detail()
    {
        var report = await Skuld.SimulateAsync(() => TestSystem.Create(Fragile), TwoOrders,
            [new("always broken", runtime => $"{runtime.Journal.Count(entry => entry.Process == "skuld")} perturbations")],
            new SimulationOptions { Runs = 1, FaultRate = 1, DuplicateRate = 1 });
        var violation = Assert.Single(report.Violations);
        var shrunk = Assert.IsType<ShrunkFuture>(violation.Shrunk);
        Assert.Empty(shrunk.Perturbations);
        Assert.Equal("0 perturbations", shrunk.Detail);
        Assert.Equal(1, shrunk.Replays);
        Assert.Contains("Smallest future that breaks it: 0 perturbations", report.ToString(), StringComparison.Ordinal);
        Assert.Contains("options.Only(1)", report.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("failed transiently", report.ToString(), StringComparison.Ordinal);
        Assert.Contains(violation.Journal, entry => entry.Message.EndsWith("failed transiently", StringComparison.Ordinal));
    }

    [Fact]
    public async Task a_replay_exception_does_not_replace_a_business_invariant_failure()
    {
        var calls = 0;
        var report = await Skuld.SimulateAsync(() => TestSystem.Create(Fragile), runtime =>
        {
            if (++calls > 1) { throw new InvalidOperationException("different failure"); }
            return TwoOrders(runtime);
        }, [ValidOrdersGoThrough], new SimulationOptions { Runs = 1, FaultRate = 1, ShrinkBudget = 3 });

        var violation = Assert.Single(report.Violations);
        Assert.Equal(ValidOrdersGoThrough.Name, violation.Invariant);
        Assert.Equal(violation.Journal, violation.Shrunk!.Journal);
        Assert.DoesNotContain("different failure", report.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task scenario_exceptions_can_also_shrink()
    {
        var report = await Skuld.SimulateAsync(() => TestSystem.Create(), _ => throw new InvalidOperationException("boom"),
            Invariant.Defaults, new SimulationOptions { Runs = 1 });
        var violation = Assert.Single(report.Violations);
        Assert.Equal("runs without errors", violation.Invariant);
        Assert.Equal("InvalidOperationException: boom", violation.Shrunk!.Detail);
        Assert.Empty(violation.Shrunk.Perturbations);
    }

    [Fact]
    public async Task negative_budget_is_rejected() =>
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Skuld.SimulateAsync(
            () => TestSystem.Create(), _ => Task.CompletedTask, [], new SimulationOptions { ShrinkBudget = -1 }));
}
