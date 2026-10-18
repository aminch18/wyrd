using System.Globalization;
using System.Text;
using Wyrd.Runtime;

namespace Wyrd.Testing;

/// <summary>What Urd found: the first point where today's code did something other than what was recorded, if any.</summary>
public sealed class ReplayReport
{
    internal ReplayReport(InstanceHistory recorded, IReadOnlyList<HistoryEntry> replayed, IReadOnlyList<JournalEntry> journal)
    {
        Recorded = recorded;
        Replayed = replayed;
        Journal = journal;
        var expected = Relative(recorded.Entries);
        var actual = Relative(replayed);
        for (var i = 0; i < Math.Max(expected.Count, actual.Count); i++)
        {
            var left = i < expected.Count ? expected[i] : null;
            var right = i < actual.Count ? actual[i] : null;
            if (Untimed(left) != Untimed(right))
            {
                Divergence = new ReplayDivergence(i, left, right);
                break;
            }
        }
    }

    public InstanceHistory Recorded { get; }

    /// <summary>The history the replay produced.</summary>
    public IReadOnlyList<HistoryEntry> Replayed { get; }

    public IReadOnlyList<JournalEntry> Journal { get; }

    /// <summary>The first entry that differs (times relative to the start), or null.</summary>
    public ReplayDivergence? Divergence { get; }

    /// <summary>Why the history could not be replayed at all, e.g. the process structure changed.</summary>
    public string? Reason { get; init; }

    /// <summary>An exception the replay threw, e.g. a recorded payload that no longer fits today's type.</summary>
    public Exception? Error { get; init; }

    public bool Diverged => Reason is not null || Error is not null || Divergence is not null;

    public void ThrowIfDiverged()
    {
        if (Diverged)
        {
            throw new WyrdAssertionException(ToString());
        }
    }

    public override string ToString()
    {
        var text = new StringBuilder();
        if (!Diverged)
        {
            return $"Urd replayed {Recorded.Process} '{Recorded.Key}': {Recorded.Entries.Count} entries, as recorded.";
        }

        text.AppendLine(CultureInfo.InvariantCulture, $"✗ {Recorded.Process} '{Recorded.Key}' does not replay as recorded.");
        if (Reason is not null)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"  {Reason}");
            return text.ToString();
        }

        if (Divergence is { } divergence)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"  First difference at entry {divergence.Index}:");
            text.AppendLine(CultureInfo.InvariantCulture, $"    recorded: {Describe(divergence.Recorded)}");
            text.AppendLine(CultureInfo.InvariantCulture, $"    replayed: {Describe(divergence.Replayed)}");
        }

        if (Error is not null)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"  The replay failed: {Error.GetType().Name}: {Error.Message}");
        }

        text.AppendLine("  Journal:");
        foreach (var entry in Journal)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"    {entry}");
        }

        return text.ToString();
    }

    /// <summary>
    /// Entries are compared without their time: on a real clock a reply takes milliseconds, on replay none. What time
    /// changes — a timeout, a retry — shows up as entries of its own.
    /// </summary>
    private static HistoryEntry? Untimed(HistoryEntry? entry) => entry is null ? null : entry with { At = default };

    /// <summary>Entries with their time made relative to the start, for the report.</summary>
    private static List<HistoryEntry> Relative(IReadOnlyList<HistoryEntry> entries) =>
        entries.Count == 0 ? [] : [.. entries.Select(e => e with { At = DateTimeOffset.UnixEpoch + (e.At - entries[0].At) })];

    private static string Describe(HistoryEntry? entry)
    {
        if (entry is null)
        {
            return "(nothing)";
        }

        var at = (entry.At - DateTimeOffset.UnixEpoch).ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
        var parts = new List<string> { $"+{at}", entry.Kind.ToString() };
        if (entry.Step is { } step)
        {
            parts.Add(entry.Compensating ? $"undo of step {step}" : $"step {step}");
        }

        if (entry.Attempt is { } attempt)
        {
            parts.Add($"attempt {attempt}");
        }

        if (entry.Outcome is not null)
        {
            parts.Add(entry.Outcome);
        }

        if (entry.Type is not null)
        {
            parts.Add($"{entry.Type} {entry.Payload}");
        }

        if (entry.Reason is not null)
        {
            parts.Add(entry.Reason);
        }

        return string.Join(" ", parts);
    }
}

/// <summary>The first entry where the replay and the recording differ; a null side means it has no such entry.</summary>
public sealed record ReplayDivergence(int Index, HistoryEntry? Recorded, HistoryEntry? Replayed);
