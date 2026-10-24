namespace Wyrd.Runtime;

/// <summary>
/// How long a host keeps one kind of data: <see cref="For"/> a period, or <see cref="Forever"/> with the reason written
/// down. There is no default: like a step's retry policy, it is a decision.
/// </summary>
public sealed record Keep
{
    private Keep(TimeSpan? period, string? reason)
    {
        Period = period;
        Reason = reason;
    }

    /// <summary>How long it is kept; null when it is kept forever.</summary>
    public TimeSpan? Period { get; }

    /// <summary>Why it is kept forever; null when it expires.</summary>
    public string? Reason { get; }

    /// <summary>Kept for <paramref name="period"/>, then removed.</summary>
    public static Keep For(TimeSpan period)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(period, TimeSpan.Zero);
        return new Keep(period, null);
    }

    /// <summary>Never removed, because <paramref name="why"/>: the table grows with every message, on purpose.</summary>
    public static Keep Forever(string why)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(why);
        return new Keep(null, why);
    }

    /// <summary>Whether something recorded at <paramref name="at"/> has expired at <paramref name="now"/>.</summary>
    public bool Expired(DateTimeOffset at, DateTimeOffset now) => Period is { } period && at + period <= now;

    /// <summary>The oldest moment still kept at <paramref name="now"/>, or null when nothing expires.</summary>
    public DateTimeOffset? Cutoff(DateTimeOffset now) => Period is { } period ? now - period : null;

    /// <summary>The decision in words: <c>for 7 days</c>, or <c>forever (why)</c>.</summary>
    public override string ToString() => Period is { } period ? $"for {Durations.Format(period)}" : $"forever ({Reason})";
}

/// <summary>
/// Everything a durable host keeps, and for how long (Q2.3). Every member is required, so each one is a decision written
/// in the application's code. Suspended instances never expire: they wait for an operator.
/// </summary>
public sealed record Retention
{
    /// <summary>
    /// The inbox and the handled message ids: how long a duplicate is still recognised. Longer than any redelivery your
    /// broker or your workers can make; afterwards the same message would be handled again. Fences are the exception:
    /// a fenced command is blocked forever.
    /// </summary>
    public required Keep Deduplication { get; init; }

    /// <summary>
    /// Completed and compensated instances, with their history (what Urd replays), counted from when they finished.
    /// Once an instance is removed its key is free: a later trigger with the same key starts a new instance.
    /// </summary>
    public required Keep FinishedInstances { get; init; }

    /// <summary>
    /// Integration events published to other services, counted from when they were relayed (or published, on a host
    /// without a transport, where they never leave).
    /// </summary>
    public required Keep RelayedMessages { get; init; }

    /// <summary>Accepted domain events, the record of business facts, counted from when they were recorded.</summary>
    public required Keep Events { get; init; }

    /// <summary>Messages that gave up after their delivery attempts, counted from when they failed.</summary>
    public required Keep DeadLetters { get; init; }

    /// <summary>
    /// For the in-memory host, where nothing outlives the process: everything is kept. Not for durable hosts.
    /// </summary>
    public static Retention InMemory { get; } = new()
    {
        Deduplication = Keep.Forever("in memory: nothing outlives the process"),
        FinishedInstances = Keep.Forever("in memory: nothing outlives the process"),
        RelayedMessages = Keep.Forever("in memory: nothing outlives the process"),
        Events = Keep.Forever("in memory: nothing outlives the process"),
        DeadLetters = Keep.Forever("in memory: nothing outlives the process"),
    };
}
