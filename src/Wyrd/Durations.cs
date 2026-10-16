using System.Globalization;

namespace Wyrd;

internal static class Durations
{
    /// <summary>Compact, readable durations: <c>15m</c>, <c>2h</c>, <c>1s</c>.</summary>
    public static string Format(TimeSpan t) => t switch
    {
        { TotalDays: >= 1 } when t.TotalDays % 1 == 0 => Invariant($"{t.TotalDays}d"),
        { TotalHours: >= 1 } when t.TotalHours % 1 == 0 => Invariant($"{t.TotalHours}h"),
        { TotalMinutes: >= 1 } when t.TotalMinutes % 1 == 0 => Invariant($"{t.TotalMinutes}m"),
        _ => Invariant($"{t.TotalSeconds}s"),
    };

    private static string Invariant(FormattableString value) => value.ToString(CultureInfo.InvariantCulture);
}
