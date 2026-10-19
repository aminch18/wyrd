namespace Wyrd;

/// <summary>How many times something is attempted. Created with <c>3.Times</c>.</summary>
public readonly record struct Attempts
{
    /// <summary>Creates a positive count for a retry policy. Counts less than one are rejected.</summary>
    public Attempts(int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        Count = count;
    }

    /// <summary>The positive count supplied to the retry policy.</summary>
    public int Count { get; }

    /// <summary>Formats the count for process diagrams and diagnostics, for example <c>3x</c>.</summary>
    public override string ToString() => $"{Count}x";
}

/// <summary>Readable time and attempt literals: <c>15.Minutes</c>, <c>3.Times</c>.</summary>
public static class Literals
{
    extension(int value)
    {
        /// <summary>Creates a duration of this many seconds, for retry and virtual-clock scenarios.</summary>
        public TimeSpan Seconds => TimeSpan.FromSeconds(value);

        /// <summary>Creates a duration of this many minutes, for business deadlines and virtual time.</summary>
        public TimeSpan Minutes => TimeSpan.FromMinutes(value);

        /// <summary>Creates a duration of this many hours.</summary>
        public TimeSpan Hours => TimeSpan.FromHours(value);

        /// <summary>Creates a duration of this many days.</summary>
        public TimeSpan Days => TimeSpan.FromDays(value);

        /// <summary>Creates a positive retry count, for example <c>.Retry(3.Times)</c>.</summary>
        public Attempts Times => new(value);
    }
}
