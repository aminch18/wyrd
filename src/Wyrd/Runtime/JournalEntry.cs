using System.Globalization;

namespace Wyrd.Runtime;

/// <summary>One line of what the runtime did, in order. Useful for tests, demos and debugging.</summary>
public sealed record JournalEntry(TimeSpan At, string Process, object? Key, string Message)
{
    /// <summary>Formats virtual time, source, key and message as one readable journal line.</summary>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"[{At:hh\\:mm\\:ss}] {Process,-16} {Key,-8} {Message}");
}
