using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace Wyrd.EntityFrameworkCore;

/// <summary>
/// How a durable host's queue is doing, measured by <see cref="EfCoreRuntime{TContext}.HealthAsync"/> and published by
/// its workers on the <c>Wyrd</c> meter. Slowness is observed, never turned into failures (semantics 3.5): alert on these.
/// </summary>
/// <param name="Due">Messages due and not handled yet.</param>
/// <param name="OldestDue">How long the oldest due message has waited: the queue's lag.</param>
/// <param name="Scheduled">Messages waiting for their time: timers, retries, redeliveries.</param>
/// <param name="DeadLetters">Messages that gave up and wait for someone to look at them.</param>
/// <param name="PendingSteps">Running instances waiting for the outcome of a step.</param>
/// <param name="OldestPendingStep">How long the oldest of those steps has been waited for, retries included.</param>
/// <param name="Suspended">Instances that need an operator.</param>
public sealed record QueueHealth(
    long Due, TimeSpan OldestDue, long Scheduled, long DeadLetters, long PendingSteps, TimeSpan OldestPendingStep, long Suspended);

/// <summary>
/// The <c>Wyrd</c> meter (collectors opt in with <c>AddMeter("Wyrd")</c>): the latest health of each queue, tagged with
/// its database, schema and table, and counters of what workers handle.
/// </summary>
internal static class QueueMetrics
{
    private static readonly Meter Meter = new("Wyrd");
    private static readonly ConcurrentDictionary<string, QueueHealth> Latest = new();

    public static readonly Counter<long> Handled = Meter.CreateCounter<long>("wyrd.messages.handled", "{message}", "Messages handled by workers");

    public static readonly Counter<long> Failed = Meter.CreateCounter<long>("wyrd.messages.failed", "{message}", "Deliveries that failed and were rolled back");

    public static readonly Counter<long> DeadLettered = Meter.CreateCounter<long>("wyrd.messages.dead_lettered", "{message}", "Messages moved to the dead letters");

    static QueueMetrics()
    {
        Count("wyrd.queue.due", "{message}", "Messages due and not handled yet", h => h.Due);
        Age("wyrd.queue.oldest_due.age", "How long the oldest due message has waited", h => h.OldestDue);
        Count("wyrd.queue.scheduled", "{message}", "Messages waiting for their time", h => h.Scheduled);
        Count("wyrd.dead_letters", "{message}", "Messages that gave up", h => h.DeadLetters);
        Count("wyrd.process.pending_steps", "{step}", "Running instances waiting for the outcome of a step", h => h.PendingSteps);
        Age("wyrd.process.oldest_pending_step.age", "How long the oldest pending step has been waited for", h => h.OldestPendingStep);
        Count("wyrd.process.suspended", "{instance}", "Instances that need an operator", h => h.Suspended);
    }

    public static void Record(string queue, QueueHealth health) => Latest[queue] = health;

    /// <summary>A disposed runtime's last health is not published forever.</summary>
    public static void Forget(string queue) => Latest.TryRemove(queue, out _);

    private static void Count(string name, string unit, string description, Func<QueueHealth, long> value) =>
        Meter.CreateObservableGauge(name, () => Latest.Select(e => new Measurement<long>(value(e.Value), Tag(e.Key))), unit, description);

    private static void Age(string name, string description, Func<QueueHealth, TimeSpan> value) =>
        Meter.CreateObservableGauge(name, () => Latest.Select(e => new Measurement<double>(value(e.Value).TotalSeconds, Tag(e.Key))), "s", description);

    private static KeyValuePair<string, object?> Tag(string queue) => new("wyrd.queue", queue);
}
