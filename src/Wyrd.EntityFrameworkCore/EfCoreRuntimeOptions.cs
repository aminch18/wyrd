namespace Wyrd.EntityFrameworkCore;

/// <summary>How the EF Core runtime delivers messages. Where Wyrd's tables live is part of the model (<c>AddWyrd</c>).</summary>
public sealed record EfCoreRuntimeOptions
{
    /// <summary>How long a worker waits before looking again when the queue is empty.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>After this many failed deliveries a message moves to the dead letters.</summary>
    public int MaxDeliveryAttempts { get; init; } = 10;

    /// <summary>The delay before a failed delivery is tried again (doubled on every attempt, capped at one minute).</summary>
    public TimeSpan RedeliveryDelay { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>How often a worker removes what the retention no longer keeps.</summary>
    public TimeSpan CleanupInterval { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>How many published messages the relay sends per transaction.</summary>
    public int RelayBatchSize { get; init; } = 100;

    /// <summary>How many times a command sent from outside is retried on a concurrency conflict.</summary>
    public int MaxConflictRetries { get; init; } = 5;
}
