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

        var violations = new List<Violation>();
        for (var run = 0; run < options.Runs; run++)
        {
            var seed = options.Seed + run;
            using var runtime = system();
            runtime.Simulation = new Future(runtime, new Random(seed), options);

            try
            {
                await scenario(runtime).ConfigureAwait(false);
                await runtime.AdvanceAsync(options.SettleFor).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                violations.Add(new Violation(seed, "runs without errors", $"{ex.GetType().Name}: {ex.Message}", runtime.Journal));
                continue;
            }

            foreach (var invariant in invariants)
            {
                if (invariant.Check(runtime) is { } detail)
                {
                    violations.Add(new Violation(seed, invariant.Name, detail, runtime.Journal));
                }
            }
        }

        return new SimulationReport(options.Runs, options, violations);
    }

    /// <summary>One future: every perturbation is drawn from a seeded random source and written to the journal.</summary>
    private sealed class Future(InMemoryRuntime runtime, Random random, SimulationOptions options) : ISimulation
    {
        private readonly HashSet<Envelope> _duplicated = [];

        public int PickNext(IReadOnlyList<Envelope> queue)
        {
            if (queue.Count < 2 || random.NextDouble() >= options.ReorderRate)
            {
                return 0;
            }

            var index = random.Next(1, queue.Count);
            runtime.Note($"delivered {runtime.Describe(queue[index])} before {runtime.Describe(queue[0])}");
            return index;
        }

        public bool Duplicate(Envelope envelope)
        {
            // Each message is redelivered at most once, so even a 100% rate always terminates.
            if (_duplicated.Contains(envelope) || random.NextDouble() >= options.DuplicateRate)
            {
                return false;
            }

            _duplicated.Add(envelope);

            runtime.Note($"will deliver {runtime.Describe(envelope)} twice");
            return true;
        }

        public bool Fault(Envelope command, Type commandType)
        {
            if (random.NextDouble() >= options.FaultRate)
            {
                return false;
            }

            runtime.Note($"{runtime.Describe(command)} failed transiently");
            return true;
        }
    }
}
