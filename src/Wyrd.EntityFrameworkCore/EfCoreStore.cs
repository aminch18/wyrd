using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Wyrd.Runtime;

namespace Wyrd.EntityFrameworkCore;

/// <summary>
/// The store for one message, bound to one context and its transaction: everything the engine reads and commits happens
/// inside it, together with taking the message off the queue. Versions give optimistic concurrency between workers.
/// </summary>
internal sealed class EfCoreStore(DbContext db, DateTimeOffset now) : IWyrdStore
{
    public async ValueTask<StoredState?> LoadStateAsync(string stateType, string key, CancellationToken cancellationToken) =>
        await db.Set<StateRow>().AsNoTracking()
            .Where(r => r.StateType == stateType && r.Key == key)
            .Select(r => new StoredState(stateType, key, r.Type, r.Payload, r.Version))
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

    public async ValueTask<StoredInstance?> LoadInstanceAsync(string process, string key, CancellationToken cancellationToken)
    {
        var row = await db.Set<InstanceRow>().AsNoTracking()
            .FirstOrDefaultAsync(r => r.Process == process && r.Key == key, cancellationToken).ConfigureAwait(false);
        return row is null ? null : Deserialize(row);
    }

    public async ValueTask<ReplyMessage?> FindReplyAsync(Guid commandId, CancellationToken cancellationToken)
    {
        var reply = await db.Set<InboxRow>().AsNoTracking()
            .Where(r => r.MessageId == commandId).Select(r => r.Reply)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return reply is null ? null : MessageTypes.DeserializeReply(reply);
    }

    public async ValueTask<StoredParked?> FindParkedAsync(string process, string key, int step, CancellationToken cancellationToken) =>
        await db.Set<ParkedRow>().AsNoTracking()
            .Where(r => r.Process == process && r.Key == key && r.Step == step)
            .Select(r => new StoredParked(process, key, step, r.Type, r.Payload))
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

    public async ValueTask<StoredView?> LoadViewAsync(string viewType, string key, CancellationToken cancellationToken) =>
        await db.Set<ViewRow>().AsNoTracking()
            .Where(r => r.ViewType == viewType && r.Key == key)
            .Select(r => new StoredView(viewType, key, r.Type, r.Payload, r.Version))
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

    public async ValueTask<long> LoadProjectedAsync(string stateType, string key, CancellationToken cancellationToken) =>
        await db.Set<ProjectedRow>().AsNoTracking()
            .Where(r => r.StateType == stateType && r.Key == key)
            .Select(r => r.Version)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

    public async ValueTask<StoredHeld?> FindHeldAsync(string stateType, string key, long version, CancellationToken cancellationToken) =>
        await db.Set<HeldRow>().AsNoTracking()
            .Where(r => r.StateType == stateType && r.Key == key && r.Version == version)
            .Select(r => new StoredHeld(stateType, key, version, r.Type, r.Payload))
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

    public async ValueTask<bool> WasHandledAsync(Guid messageId, CancellationToken cancellationToken) =>
        await db.Set<HandledRow>().AnyAsync(r => r.MessageId == messageId, cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Writes one message's changes. Versioned rows are updated with their expected version in the WHERE clause; new rows
    /// are inserted on <c>SaveChanges</c>, where a unique key taken by another worker fails the whole transaction (the
    /// runtime retries the message against what the other worker committed).
    /// </summary>
    public async ValueTask CommitAsync(Changes changes, CancellationToken cancellationToken)
    {
        foreach (var state in changes.States)
        {
            await SaveStateAsync(state, cancellationToken).ConfigureAwait(false);
        }

        foreach (var instance in changes.Instances.Values)
        {
            await SaveInstanceAsync(instance, cancellationToken).ConfigureAwait(false);
        }

        foreach (var (id, reply) in changes.Replies)
        {
            db.Add(new InboxRow { MessageId = id, Reply = MessageTypes.SerializeReply(reply), RecordedAt = now, Fence = reply.Outcome == ReplyOutcome.Fenced });
        }

        foreach (var id in changes.Handled)
        {
            db.Add(new HandledRow { MessageId = id, HandledAt = now });
        }

        foreach (var e in changes.Events)
        {
            db.Add(new EventRow { Type = e.Type, Payload = e.Payload, RecordedAt = now });
        }

        foreach (var e in changes.Published)
        {
            db.Add(new PublishedRow
            {
                MessageId = e.Id, Type = e.Type, Payload = e.Payload, RecordedAt = now, TraceParent = e.Trace?.Parent, TraceState = e.Trace?.State,
            });
        }

        foreach (var parked in changes.Park)
        {
            // An event already parked for this step stays: the first one to arrive is the one the step takes.
            if (!await db.Set<ParkedRow>().AnyAsync(r => r.Process == parked.Process && r.Key == parked.Key && r.Step == parked.Step, cancellationToken).ConfigureAwait(false))
            {
                db.Add(new ParkedRow { Process = parked.Process, Key = parked.Key, Step = parked.Step, Type = parked.Type, Payload = parked.Payload });
            }
        }

        foreach (var parked in changes.Unpark)
        {
            await db.Set<ParkedRow>()
                .Where(r => r.Process == parked.Process && r.Key == parked.Key && r.Step == parked.Step)
                .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var view in changes.Views.Values)
        {
            await SaveViewAsync(view, cancellationToken).ConfigureAwait(false);
        }

        // Ordered read models: the last version of each stream projected, checked like any other version.
        foreach (var ((stateType, key), (read, version)) in changes.Projected)
        {
            if (read == 0)
            {
                db.Add(new ProjectedRow { StateType = stateType, Key = key, Version = version });
            }
            else if (await db.Set<ProjectedRow>()
                .Where(r => r.StateType == stateType && r.Key == key && r.Version == read)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Version, version), cancellationToken).ConfigureAwait(false) != 1)
            {
                throw new ConcurrencyConflictException($"Concurrency conflict projecting {stateType} '{key}'.");
            }
        }

        foreach (var held in changes.Hold)
        {
            if (!await db.Set<HeldRow>().AnyAsync(r => r.StateType == held.StateType && r.Key == held.Key && r.Version == held.Version, cancellationToken).ConfigureAwait(false))
            {
                db.Add(new HeldRow { StateType = held.StateType, Key = held.Key, Version = held.Version, Type = held.Type, Payload = held.Payload });
            }
        }

        foreach (var held in changes.Release)
        {
            await db.Set<HeldRow>()
                .Where(r => r.StateType == held.StateType && r.Key == held.Key && r.Version == held.Version)
                .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        }

        // The outbox and the timers are the queue: committed with everything else, delivered by the workers.
        foreach (var envelope in changes.Outbox)
        {
            Enqueue(envelope, now);
        }

        foreach (var scheduled in changes.Schedule)
        {
            Enqueue(scheduled.Envelope, scheduled.At);
        }

        if (changes.Cancel.Count > 0)
        {
            await db.Set<QueueRow>().Where(r => changes.Cancel.Contains(r.MessageId))
                .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Adds a message to the queue; it is written on the next <c>SaveChanges</c>.</summary>
    public void Enqueue(Envelope envelope, DateTimeOffset availableAt) =>
        db.Add(new QueueRow
        {
            MessageId = envelope.MessageId, Envelope = JsonSerializer.Serialize(envelope, WyrdJsonContext.Default.Envelope), AvailableAt = availableAt,
        });

    internal static StoredInstance Deserialize(InstanceRow row) =>
        JsonSerializer.Deserialize(row.Data, WyrdJsonContext.Default.StoredInstance)! with { Version = row.Version };

    private async ValueTask SaveStateAsync(StoredState state, CancellationToken cancellationToken)
    {
        // StoredState.Version is the version being written; the one read is Version - 1. A new state (version 1) is
        // inserted: a concurrent insert of the same key fails on SaveChanges.
        if (state.Version == 1)
        {
            db.Add(new StateRow { StateType = state.StateType, Key = state.Key, Type = state.Type, Payload = state.Payload, Version = 1 });
            return;
        }

        var rows = await db.Set<StateRow>()
            .Where(r => r.StateType == state.StateType && r.Key == state.Key && r.Version == state.Version - 1)
            .ExecuteUpdateAsync(
                s => s.SetProperty(r => r.Type, state.Type).SetProperty(r => r.Payload, state.Payload).SetProperty(r => r.Version, state.Version),
                cancellationToken).ConfigureAwait(false);
        if (rows != 1)
        {
            throw new ConcurrencyConflictException($"Concurrency conflict on {state.StateType} '{state.Key}'.");
        }
    }

    private async ValueTask SaveInstanceAsync(StoredInstance instance, CancellationToken cancellationToken)
    {
        // StoredInstance.Version is the version read (0 when new).
        var data = JsonSerializer.Serialize(instance, WyrdJsonContext.Default.StoredInstance);
        var status = instance.Status.ToString();
        DateTimeOffset? finishedAt = instance.Status is ProcessStatus.Completed or ProcessStatus.Compensated ? now : null;
        var pendingSince = instance is { Status: ProcessStatus.Running, Pending: not null }
            ? instance.History.LastOrDefault(e => e.Kind == HistoryKind.Sent && e.Attempt == 1)?.At
            : null;
        if (instance.Version == 0)
        {
            db.Add(new InstanceRow
            {
                Process = instance.Process, Key = instance.Key, Status = status, Deadline = instance.Deadline, Data = data, Version = 1, FinishedAt = finishedAt,
                PendingSince = pendingSince,
            });
            return;
        }

        var rows = await db.Set<InstanceRow>()
            .Where(r => r.Process == instance.Process && r.Key == instance.Key && r.Version == instance.Version)
            .ExecuteUpdateAsync(
                s => s.SetProperty(r => r.Status, status)
                    .SetProperty(r => r.Deadline, instance.Deadline)
                    .SetProperty(r => r.FinishedAt, finishedAt)
                    .SetProperty(r => r.PendingSince, pendingSince)
                    .SetProperty(r => r.Data, data)
                    .SetProperty(r => r.Version, instance.Version + 1),
                cancellationToken).ConfigureAwait(false);
        if (rows != 1)
        {
            throw new ConcurrencyConflictException($"Concurrency conflict on process '{instance.Process}' '{instance.Key}'.");
        }
    }

    private async ValueTask SaveViewAsync(StoredView view, CancellationToken cancellationToken)
    {
        // Versioned like states: two workers projecting different streams into one view must not overwrite each other.
        // A new view is inserted, and a concurrent insert of the same key fails on SaveChanges.
        if (view.Version == 0)
        {
            if (view.Payload is not null)
            {
                db.Add(new ViewRow { ViewType = view.ViewType, Key = view.Key, Type = view.Type!, Payload = view.Payload, Created = now, Version = 1 });
            }

            return;
        }

        var read = db.Set<ViewRow>().Where(r => r.ViewType == view.ViewType && r.Key == view.Key && r.Version == view.Version);
        var rows = view.Payload is null
            ? await read.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false)
            : await read.ExecuteUpdateAsync(
                s => s.SetProperty(r => r.Type, view.Type!).SetProperty(r => r.Payload, view.Payload).SetProperty(r => r.Version, view.Version + 1),
                cancellationToken).ConfigureAwait(false);
        if (rows != 1)
        {
            throw new ConcurrencyConflictException($"Concurrency conflict on read model {view.ViewType} '{view.Key}'.");
        }
    }
}
