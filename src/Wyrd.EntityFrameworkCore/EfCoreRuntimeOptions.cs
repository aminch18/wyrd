namespace Wyrd.EntityFrameworkCore;

/// <summary>How the EF Core runtime delivers messages. Where Wyrd's tables live is part of the model (<c>AddWyrd</c>).</summary>
public sealed record EfCoreRuntimeOptions
{
    /// <summary>
    /// How long a worker waits before looking again when the queue is empty. The wait doubles while the queue stays
    /// empty, up to <see cref="MaxPollInterval"/>, and is back to this as soon as there is work. A message this runtime
    /// queues itself wakes its workers at once, except one sent into your own transaction: that one is seen on the next
    /// look, after you commit. Must be positive, and no longer than <see cref="MaxPollInterval"/>.
    /// </summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>The longest wait of an idle worker: how late, at most, it sees a message queued by another process.</summary>
    public TimeSpan MaxPollInterval { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How often workers measure the queue's health for the <c>Wyrd</c> meter (<see cref="QueueHealth"/>). Each
    /// measurement counts the queue, and the counts cost more the longer the backlog they report: with many runtime
    /// instances and large backlogs, measure less often. Must be positive.
    /// </summary>
    public TimeSpan HealthInterval { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>After this many failed deliveries a message moves to the dead letters.</summary>
    public int MaxDeliveryAttempts { get; init; } = 10;

    /// <summary>The delay before a failed delivery is tried again (doubled on every attempt, capped at one minute).</summary>
    public TimeSpan RedeliveryDelay { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// How often a worker removes what the retention no longer keeps. A pass removes a bounded amount (ten batches of
    /// each kind); when more expired, the next loop goes on, between messages. Must be positive.
    /// </summary>
    public TimeSpan CleanupInterval { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>How many published messages the relay sends per transaction.</summary>
    public int RelayBatchSize { get; init; } = 100;

    /// <summary>
    /// How long a worker holds an integration command while it calls the outside world, with no transaction open. A
    /// worker that stops meanwhile leaves it to another one after this time, which calls again with the same idempotency
    /// key; so does a call that takes longer. Longer than your slowest integration call.
    /// </summary>
    public TimeSpan IntegrationLease { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>How many times a command sent from outside is retried on a concurrency conflict.</summary>
    public int MaxConflictRetries { get; init; } = 5;
}
