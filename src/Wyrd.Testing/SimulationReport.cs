using System.Globalization;
using System.Text;
using Wyrd.Runtime;

namespace Wyrd.Testing;

/// <summary>A future in which an invariant did not hold.</summary>
public sealed record Violation(int Seed, string Invariant, string Detail, IReadOnlyList<JournalEntry> Journal);

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
            text.AppendLine(CultureInfo.InvariantCulture, $"  {violation.Detail}");
            var perturbations = violation.Journal.Where(e => e.Process == "skuld").Select(e => e.Message).ToList();
            if (perturbations.Count > 0)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"  Perturbations: {string.Join("; ", perturbations)}");
            }

            text.AppendLine("  Journal:");
            foreach (var entry in violation.Journal)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"    {entry}");
            }

            text.AppendLine(CultureInfo.InvariantCulture, $"  Replay it: Skuld.SimulateAsync(..., options.Only({violation.Seed}))");
        }

        return text.ToString();
    }
}
