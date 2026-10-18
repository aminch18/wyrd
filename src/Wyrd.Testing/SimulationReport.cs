using System.Globalization;
using System.Text;
using Wyrd.Runtime;

namespace Wyrd.Testing;

/// <summary>A future in which an invariant did not hold.</summary>
public sealed record Violation(int Seed, string Invariant, string Detail, IReadOnlyList<JournalEntry> Journal)
{
    /// <summary>
    /// The smallest failing future found within the budget, for the first violation of each invariant (the one the
    /// report shows); null otherwise, or when shrinking is disabled.
    /// </summary>
    public ShrunkFuture? Shrunk { get; init; }
}

/// <summary>A reduced future that still breaks the same invariant. Greedy shrinking need not find a global minimum.</summary>
public sealed record ShrunkFuture(
    string Detail, IReadOnlyList<string> Perturbations, IReadOnlyList<JournalEntry> Journal, int Replays);

/// <summary>What Skuld found.</summary>
public sealed class SimulationReport(int runs, SimulationOptions options, IReadOnlyList<Violation> violations)
{
    public int Runs { get; } = runs;

    public SimulationOptions Options { get; } = options;

    public IReadOnlyList<Violation> Violations { get; } = violations;

    public bool Passed => Violations.Count == 0;

    /// <summary>Fails the test with the first broken future of each invariant: perturbations, journal and the seed to replay it.</summary>
    public void ThrowIfViolated()
    {
        if (!Passed)
        {
            throw new WyrdAssertionException(ToString());
        }
    }

    public override string ToString()
    {
        var text = new StringBuilder();
        var broken = Violations.Select(v => v.Seed).Distinct().Count();
        text.AppendLine(CultureInfo.InvariantCulture, $"Skuld explored {Runs} futures: {(Passed ? "every invariant held." : $"{broken} broke an invariant.")}");

        foreach (var violation in Violations.GroupBy(v => v.Invariant).Select(g => g.First()))
        {
            var count = Violations.Count(v => v.Invariant == violation.Invariant);
            text.AppendLine();
            text.AppendLine(CultureInfo.InvariantCulture, $"✗ \"{violation.Invariant}\" broke in {count} futures. First: seed {violation.Seed}.");
            if (violation.Shrunk is { } shrunk)
            {
                text.AppendLine(CultureInfo.InvariantCulture,
                    $"  Smallest future that breaks it: {Count(shrunk.Perturbations.Count, "perturbation")} ({Count(shrunk.Replays, "replay")}; smallest found within budget).");
            }

            text.AppendLine(CultureInfo.InvariantCulture, $"  {violation.Shrunk?.Detail ?? violation.Detail}");
            var journal = violation.Shrunk?.Journal ?? violation.Journal;
            var perturbations = violation.Shrunk?.Perturbations
                ?? journal.Where(e => e.Process == "skuld").Select(e => e.Message).ToArray();
            if (perturbations.Count > 0)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"  Perturbations: {string.Join("; ", perturbations)}");
            }

            text.AppendLine("  Journal:");
            foreach (var entry in journal)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"    {entry}");
            }

            text.AppendLine(CultureInfo.InvariantCulture, $"  Replay it: Skuld.SimulateAsync(..., options.Only({violation.Seed}))");
        }

        return text.ToString();
    }

    private static string Count(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";
}
