using Wyrd.Runtime;

namespace Wyrd.Testing;

/// <summary>
/// Skuld, the Norn of what will be. Runs a scenario in many futures — commands that fail transiently, messages
/// delivered twice or out of order — and checks invariants at the end of each one. Deterministic: a seed
/// identifies a future, so any failure can be replayed exactly.
/// </summary>
/// <example>
/// <code>
/// var report = await Skuld.SimulateAsync(
///     ShopModule.CreateRuntime,
///     async shop =>
///     {
///         await shop.SendAsync(new Restock("KEYBOARD", 5));
///         await shop.SendAsync(new PlaceOrder("A-1", "KEYBOARD", 1, 89m));
///     },
///     [.. Invariant.Defaults, new("stock is conserved", shop => ...)]);
///
/// report.ThrowIfViolated();
/// </code>
/// </example>
public static class Skuld
{
    /// <summary>Creates a fresh runtime for each seeded future, runs the scenario with hostile deliveries, and checks the supplied invariants after settling virtual time.</summary>
    /// <remarks>Keep the runtime factory and scenario reproducible so shrinking can replay the same starting conditions.</remarks>
    public static async Task<SimulationReport> SimulateAsync(
        Func<InMemoryRuntime> system,
        Func<InMemoryRuntime, Task> scenario,
        IReadOnlyList<Invariant> invariants,
        SimulationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(invariants);
        options ??= new SimulationOptions();
        options.Validate();

        var violations = new List<Violation>();
        for (var run = 0; run < options.Runs; run++)
        {
            var seed = options.Seed + run;
            using var runtime = system();
            var decisions = new SimulationDecisions(runtime, new Random(seed), options);
            runtime.Simulation = decisions;
            var error = await RunAsync(runtime, scenario, options).ConfigureAwait(false);
            var broken = error is null
                ? invariants.Select(invariant => (Invariant: invariant, Detail: invariant.Check(runtime)))
                    .Where(result => result.Detail is not null).ToArray()
                : [(Invariant: new Invariant("runs without errors", _ => Describe(error)), Detail: (string?)Describe(error))];
            var remaining = options.ShrinkBudget;

            foreach (var (invariant, failure) in broken)
            {
                var detail = failure!;
                var violation = new Violation(seed, invariant.Name, detail, runtime.Journal);

                // The report shows the first future that breaks each invariant: only that one is worth shrinking.
                if (options.Shrink && !violations.Any(v => v.Invariant == invariant.Name))
                {
                    var original = new ShrunkFuture(detail, Perturbations(violation.Journal), violation.Journal, 0);
                    var shrunk = await FutureShrinker.ShrinkAsync(decisions.Recorded, original, async stream =>
                    {
                        using var replay = system();
                        replay.Simulation = new SimulationDecisions(replay, new Random(seed), options, stream);
                        var replayError = await RunAsync(replay, scenario, options).ConfigureAwait(false);
                        var replayDetail = CheckFailure(invariant, replay, error, replayError);
                        return replayDetail is null ? null
                            : new ShrunkFuture(replayDetail, Perturbations(replay.Journal), replay.Journal, 0);
                    }, remaining).ConfigureAwait(false);
                    remaining -= shrunk.Replays;
                    violation = violation with { Shrunk = shrunk };
                }

                violations.Add(violation);
            }
        }

        return new SimulationReport(options.Runs, options, violations);
    }

    private static string? CheckFailure(
        Invariant invariant, InMemoryRuntime runtime, Exception? originalError, Exception? replayError)
    {
        if (originalError is null)
        {
            // An exception is not evidence that the original business invariant still breaks.
            return replayError is null ? invariant.Check(runtime) : null;
        }

        return replayError?.GetType() == originalError.GetType() && replayError.Message == originalError.Message
            ? Describe(replayError) : null;
    }

    private static string[] Perturbations(IReadOnlyList<JournalEntry> journal) =>
        journal.Where(entry => entry.Process == "skuld").Select(entry => entry.Message).ToArray();

    private static string Describe(Exception error) => $"{error.GetType().Name}: {error.Message}";

    private static async Task<Exception?> RunAsync(
        InMemoryRuntime runtime, Func<InMemoryRuntime, Task> scenario, SimulationOptions options)
    {
        try
        {
            await scenario(runtime).ConfigureAwait(false);
            await runtime.AdvanceAsync(options.SettleFor).ConfigureAwait(false);
            return null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return ex;
        }
    }
}
