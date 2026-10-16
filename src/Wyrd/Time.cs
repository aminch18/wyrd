namespace Wyrd;

/// <summary>How many times something is attempted. Created with <c>3.Times</c>.</summary>
public readonly record struct Attempts
{
    public Attempts(int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        Count = count;
    }

    public int Count { get; }

    public override string ToString() => $"{Count}x";
}

/// <summary>Readable time and attempt literals: <c>15.Minutes</c>, <c>3.Times</c>.</summary>
public static class Literals
{
    extension(int value)
    {
        public TimeSpan Seconds => TimeSpan.FromSeconds(value);

        public TimeSpan Minutes => TimeSpan.FromMinutes(value);

        public TimeSpan Hours => TimeSpan.FromHours(value);

        public TimeSpan Days => TimeSpan.FromDays(value);

        public Attempts Times => new(value);
    }
}
