using System.Collections.Immutable;
using Wyrd.Processes;

namespace Wyrd.Runtime;

/// <summary>
/// Handles one message at a time and commits what it changed atomically. It knows nothing about where
/// messages come from or where state lives: the in-memory runtime and future database/broker hosts drive it.
/// </summary>
internal sealed class Engine(
    Registry registry,
    IWyrdStore store,
    MessageTypes types,
    Func<DateTimeOffset> now,
    Func<Guid> newId,
    Func<Envelope, Type, bool> fault,
    Action<string, string?, string> log)
{
    public const string External = "-";
    private const string ReplyType = "wyrd.reply";
    private const string TimeoutType = "wyrd.timeout";

    public async ValueTask<Decision?> HandleAsync(Envelope envelope, CancellationToken cancellationToken)
    {
        using var trace = Tracing.Begin(envelope);
        try
        {
            registry.Validate();
            switch (envelope.Kind)
            {
                case MessageKind.Command:
                    return await HandleCommandAsync(envelope, cancellationToken).ConfigureAwait(false);
                case MessageKind.Event:
                    await HandleEventAsync(envelope, cancellationToken).ConfigureAwait(false);
                    return null;
                case MessageKind.Reply:
                    await HandleReplyAsync(envelope, cancellationToken).ConfigureAwait(false);
                    return null;
                case MessageKind.Timeout:
                    await HandleTimeoutAsync(envelope, cancellationToken).ConfigureAwait(false);
                    return null;
                default:
                    throw new InvalidOperationException($"Unknown message kind {envelope.Kind}.");
            }
        }
        catch (Exception exception)
        {
            trace?.Failed(exception);
            throw;
        }
    }

    // ── Commands: the owner decides ───────────────────────────────────────────────────────────

    private async ValueTask<Decision?> HandleCommandAsync(Envelope envelope, CancellationToken cancellationToken)
    {
        var command = (ICommand)types.Deserialize(envelope.Type, envelope.Payload);
        var owner = registry.OwnerOf(command.GetType());
        var key = Keys.Of(owner.KeyOf(command));
        Tracing.Key(key);
        var changes = new Changes();

        if (await store.FindReplyAsync(envelope.MessageId, cancellationToken).ConfigureAwait(false) is { } stored)
        {
            log(External, key, stored.Outcome == ReplyOutcome.Fenced
                ? $"late {command.GetType().Name} arrived after being fenced: never executed"
                : $"duplicate {command.GetType().Name} ignored, stored outcome replayed");
            Tracing.Outcome(stored.Outcome == ReplyOutcome.Fenced ? "fenced" : "duplicate", stored.Outcome);
            Reply(envelope, stored, changes);
            await store.CommitAsync(changes, cancellationToken).ConfigureAwait(false);
            return null;
        }

        if (envelope.Process is not null && fault(envelope, command.GetType()))
        {
            Tracing.Outcome(ReplyOutcome.Faulted);
            Reply(envelope, new ReplyMessage(ReplyOutcome.Faulted), changes);
            await store.CommitAsync(changes, cancellationToken).ConfigureAwait(false);
            return null;
        }

        if (envelope.Fences is { } original && await FenceAsync(envelope, original, command, key, changes, cancellationToken).ConfigureAwait(false))
        {
            await store.CommitAsync(changes, cancellationToken).ConfigureAwait(false);
            return null;
        }

        Decision decision;
        if (owner is IIntegrationHandler integration)
        {
            // The only I/O: outside world first, then the outcome is recorded like any decision. A crash in between
            // redelivers the command with the same id — the idempotency key — so the other system doesn't repeat it.
            using var call = Tracing.Integration(envelope);
            try
            {
                decision = await integration.ExecuteAsync(command, new IntegrationContext(envelope.MessageId, envelope.Process?.Attempt ?? 1), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (envelope.Process is not null && ex is not OperationCanceledException)
            {
                Tracing.Failed(call, ex);
                log(External, key, $"{command.GetType().Name} failed: {ex.GetType().Name}: {ex.Message}");
                Tracing.Outcome(ReplyOutcome.Faulted);
                Reply(envelope, new ReplyMessage(ReplyOutcome.Faulted), changes);
                await store.CommitAsync(changes, cancellationToken).ConfigureAwait(false);
                return null;
            }

            if (decision is Decision.Accepted done)
            {
                Accept(done.Event, owner, key, stream: null, changes);
            }
        }
        else
        {
            var slice = (ISliceHandler)owner;
            var stateType = slice.State.FullName!;
            var current = await store.LoadStateAsync(stateType, key, cancellationToken).ConfigureAwait(false);
            var state = current is null ? slice.Initial : types.Deserialize(current.Type, current.Payload);
            decision = slice.Decide(command, state);
            if (decision is Decision.Accepted accepted)
            {
                var version = (current?.Version ?? 0) + 1;
                var (nextType, nextPayload) = types.Serialize(((IState)state).Apply(accepted.Event), slice.State);
                changes.States.Add(new StoredState(stateType, key, nextType, nextPayload, version));
                Accept(accepted.Event, owner, key, new StreamRef(stateType, key, version), changes);
            }
        }

        ReplyMessage reply;
        if (decision is Decision.Accepted { Event: var @event })
        {
            var (eventType, eventPayload) = types.Serialize(@event, owner.Event);
            reply = new ReplyMessage(ReplyOutcome.Accepted, eventType, eventPayload);
        }
        else
        {
            var (errorType, errorPayload) = types.Serialize(((Decision.Rejected)decision).Error, owner.Error);
            reply = new ReplyMessage(ReplyOutcome.Rejected, errorType, errorPayload);
        }

        Tracing.Outcome(reply.Outcome);
        changes.Replies[envelope.MessageId] = reply;
        Reply(envelope, reply, changes);
        await store.CommitAsync(changes, cancellationToken).ConfigureAwait(false);
        return decision;
    }

    /// <summary>Records an accepted event: stored, delivered to whoever reacts to it, and mapped to its publications.</summary>
    private void Accept(IDomainEvent @event, ICommandOwner owner, string key, StreamRef? stream, Changes changes)
    {
        var (eventType, eventPayload) = types.Serialize(@event, owner.Event);
        changes.Events.Add(new StoredMessage(eventType, eventPayload));
        changes.Outbox.Add(new Envelope(newId(), MessageKind.Event, eventType, eventPayload, Stream: stream, Trace: Tracing.Capture()));

        foreach (var publication in registry.PublicationsOf(@event.GetType()))
        {
            var published = publication.Map(@event);
            var (publishedType, publishedPayload) = types.Serialize(published, published.GetType());
            var id = newId();
            var trace = Tracing.Capture();
            changes.Published.Add(new PublishedMessage(id, publishedType, publishedPayload, trace));
            changes.Outbox.Add(new Envelope(id, MessageKind.Event, publishedType, publishedPayload, Trace: trace));
            log(External, key, $"published {types.TypeOf(publishedType).Name}");
        }
    }

    /// <summary>
    /// The undo of a step with an unknown outcome. In the same transaction as the inbox: if the original command never
    /// ran, it is fenced (a late copy will find the fence and never execute) and there is nothing to undo; if it ran
    /// and was accepted, the undo runs normally. Returns true when the undo is answered without running.
    /// </summary>
    private async ValueTask<bool> FenceAsync(
        Envelope undo, Guid original, ICommand command, string key, Changes changes, CancellationToken cancellationToken)
    {
        var outcome = await store.FindReplyAsync(original, cancellationToken).ConfigureAwait(false);
        if (outcome is { Outcome: ReplyOutcome.Accepted })
        {
            return false;
        }

        if (outcome is null)
        {
            changes.Replies[original] = new ReplyMessage(ReplyOutcome.Fenced);
            log(External, key, $"{command.GetType().Name}: the original command never ran, fenced");
        }

        Tracing.Outcome("NothingToUndo");
        var reply = new ReplyMessage(ReplyOutcome.NothingToUndo);
        changes.Replies[undo.MessageId] = reply;
        Reply(undo, reply, changes);
        return true;
    }

    private void Reply(Envelope command, ReplyMessage reply, Changes changes)
    {
        if (command.Process is not null)
        {
            changes.Outbox.Add(ReplyTo(command, reply));
        }
    }

    /// <summary>
    /// The failure reply a durable host sends when it can't handle a process command: the process's retry policy
    /// decides what happens next, so hosts never retry behind its back (one retry layer).
    /// </summary>
    internal static Envelope Faulted(Envelope command, Guid id) =>
        new(id, MessageKind.Reply, ReplyType, MessageTypes.SerializeReply(new ReplyMessage(ReplyOutcome.Faulted)), command.Process, command.MessageId);

    /// <summary>The reply envelope to a process command (also used by Urd to answer from a recording).</summary>
    internal Envelope ReplyTo(Envelope command, ReplyMessage reply) =>
        new(newId(), MessageKind.Reply, ReplyType, MessageTypes.SerializeReply(reply), command.Process, command.MessageId, Trace: Tracing.ReplyContext(command));

    // ── Events: start processes, resume waits, park early arrivals ─────────────────────────────

    private async ValueTask HandleEventAsync(Envelope envelope, CancellationToken cancellationToken)
    {
        if (await store.WasHandledAsync(envelope.MessageId, cancellationToken).ConfigureAwait(false))
        {
            Tracing.Outcome("duplicate");
            return; // Redelivery of a message we already handled.
        }

        var @event = (IEvent)types.Deserialize(envelope.Type, envelope.Payload);
        var changes = new Changes();
        changes.Handled.Add(envelope.MessageId);

        await ProjectInOrderAsync(envelope, @event, changes, cancellationToken).ConfigureAwait(false);

        foreach (var process in registry.Active)
        {
            if (Accept(@event, process.Trigger) is { } trigger)
            {
                await StartAsync(process, trigger, changes, cancellationToken).ConfigureAwait(false);
            }

            for (var step = 0; step < process.Nodes.Length; step++)
            {
                if (process.Nodes[step] is AwaitNode wait && Accept(@event, wait.Event) is { } awaited)
                {
                    await ResumeOrParkAsync(process, step, wait, awaited, changes, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        await store.CommitAsync(changes, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Read models get the events of one stream in the order they happened, whatever the delivery order: an event that
    /// arrives before an earlier one of its stream is held, and projected once the gap is filled. Processes don't wait.
    /// </summary>
    private async ValueTask ProjectInOrderAsync(Envelope envelope, IEvent @event, Changes changes, CancellationToken cancellationToken)
    {
        if (registry.Projections.Count == 0)
        {
            return;
        }

        if (envelope.Stream is not { } stream)
        {
            await ProjectAsync(@event, changes, cancellationToken).ConfigureAwait(false); // From outside: no stream to order by.
            return;
        }

        var projected = await store.LoadProjectedAsync(stream.StateType, stream.Key, cancellationToken).ConfigureAwait(false);
        if (stream.Version <= projected)
        {
            return;
        }

        if (stream.Version > projected + 1)
        {
            log(External, stream.Key, $"{@event.GetType().Name} held for read models until earlier events of its stream arrive");
            changes.Hold.Add(new StoredHeld(stream.StateType, stream.Key, stream.Version, envelope.Type, envelope.Payload));
            return;
        }

        await ProjectAsync(@event, changes, cancellationToken).ConfigureAwait(false);
        var version = stream.Version;
        while (await store.FindHeldAsync(stream.StateType, stream.Key, version + 1, cancellationToken).ConfigureAwait(false) is { } held)
        {
            changes.Release.Add(held);
            await ProjectAsync((IEvent)types.Deserialize(held.Type, held.Payload), changes, cancellationToken).ConfigureAwait(false);
            version++;
        }

        changes.Projected[(stream.StateType, stream.Key)] = (projected, version);
    }

    private async ValueTask ProjectAsync(IEvent @event, Changes changes, CancellationToken cancellationToken)
    {
        foreach (var projection in registry.Projections)
        {
            foreach (var handled in projection.Events)
            {
                if (Accept(@event, handled) is { } projected)
                {
                    await ProjectAsync(projection, projected, changes, cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    private async ValueTask ProjectAsync(IProjectionHandler projection, IEvent @event, Changes changes, CancellationToken cancellationToken)
    {
        var viewType = projection.View.FullName!;
        var key = Keys.Of(projection.KeyOf(@event));
        var stored = changes.Views.TryGetValue((viewType, key), out var pending)
            ? pending
            : await store.LoadViewAsync(viewType, key, cancellationToken).ConfigureAwait(false);
        var current = stored?.Payload is null ? null : types.Deserialize(stored.Type!, stored.Payload);

        var next = projection.Apply(current, @event);
        var read = stored?.Version ?? 0;
        if (next is null)
        {
            changes.Views[(viewType, key)] = new StoredView(viewType, key, null, null, read);
            return;
        }

        var (type, payload) = types.Serialize(next, projection.View);
        changes.Views[(viewType, key)] = new StoredView(viewType, key, type, payload, read);
    }

    private async ValueTask StartAsync(Process process, IEvent trigger, Changes changes, CancellationToken cancellationToken)
    {
        var key = Keys.Of(process.KeyOf(trigger));
        if (changes.Find(process.Name, key) is not null
            || await store.LoadInstanceAsync(process.Name, key, cancellationToken).ConfigureAwait(false) is not null)
        {
            log(process.Name, key, $"duplicate {trigger.GetType().Name} ignored");
            return;
        }

        var (triggerType, triggerPayload) = types.Serialize(trigger, process.Trigger);
        var instance = new StoredInstance(
            process.Name, key, triggerType, triggerPayload, 0, ProcessStatus.Running, [], ImmutableDictionary<int, Guid>.Empty, [], 0, null, false, null,
            process.Fingerprint, [new HistoryEntry(now(), HistoryKind.Started) { Type = triggerType, Payload = triggerPayload }], 0, Trace: Tracing.Capture());
        Tracing.Instance(instance);
        Tracing.Event("wyrd.started", instance);
        log(process.Name, key, $"started by {trigger.GetType().Name}");
        changes.Save(await AdvanceAsync(process, instance, trigger, changes, cancellationToken).ConfigureAwait(false));
    }

    private async ValueTask ResumeOrParkAsync(
        Process process, int step, AwaitNode wait, IEvent awaited, Changes changes, CancellationToken cancellationToken)
    {
        var key = Keys.Of(wait.KeyOf!(awaited));
        var instance = changes.Find(process.Name, key)
            ?? await store.LoadInstanceAsync(process.Name, key, cancellationToken).ConfigureAwait(false);

        if (instance is not null)
        {
            Tracing.Instance(instance);
        }

        if (instance is { Status: ProcessStatus.Awaiting } && DefinitionChanged(process, instance) is { } changed)
        {
            // Kept at the step the instance waits at, in case it is what it waits for once its definition is back.
            changes.Park.Add(Parked(process, key, instance.Position, wait, awaited));
            changes.Save(EndWait(changed, changes));
        }
        else if (instance is { Status: ProcessStatus.Awaiting } && instance.Position == step)
        {
            log(process.Name, key, $"<- {awaited.GetType().Name}");
            var trigger = Trigger(instance);
            var fact = Parked(process, key, step, wait, awaited).AsFact();
            instance = EndWait(Record(instance, HistoryKind.Received, step: step, message: fact), changes) with
            {
                Position = step + 1, Status = ProcessStatus.Running, Facts = instance.Facts.Add(fact),
            };
            changes.Save(await AdvanceAsync(process, instance, trigger, changes, cancellationToken).ConfigureAwait(false));
        }
        else if (instance is null
            || (instance is { Status: ProcessStatus.Running, Compensating: false } && instance.Position < step)
            || (instance is { Status: ProcessStatus.Suspended, Compensating: false } && instance.Position <= step))
        {
            // Arrived before the process was waiting for it (common in distributed systems), or while it is
            // suspended: keep it for when the process gets there.
            log(process.Name, key, $"{awaited.GetType().Name} arrived early, parked");
            changes.Park.Add(Parked(process, key, step, wait, awaited));
        }
    }

    private StoredParked Parked(Process process, string key, int step, AwaitNode wait, IEvent awaited)
    {
        var (type, payload) = types.Serialize(awaited, wait.Event);
        return new StoredParked(process.Name, key, step, type, payload);
    }

    /// <summary>
    /// A wait is over before its time limit: its timer is cancelled with the rest of the changes, so nothing is left
    /// scheduled for an instance that no longer waits (Q2.2).
    /// </summary>
    private static StoredInstance EndWait(StoredInstance instance, Changes changes)
    {
        if (instance.Timer is { } timer)
        {
            changes.Cancel.Add(timer);
        }

        return instance with { Deadline = null, Timer = null };
    }

    /// <summary>
    /// The instance suspended, when the deployed definition's structure is not the one it started with: continuing
    /// would run the wrong step. Null when it is the same.
    /// </summary>
    private static StoredInstance KeepReply(StoredInstance instance, Envelope envelope, ReplyMessage reply) =>
        // A fault says the command didn't run: re-sending it is safe, there is nothing to keep.
        reply.Outcome == ReplyOutcome.Faulted ? instance : instance with { KeptReply = envelope };

    private StoredInstance? DefinitionChanged(Process process, StoredInstance instance) =>
        instance.Fingerprint == process.Fingerprint
            ? null
            : Suspend(instance, $"definition changed (started as {instance.Fingerprint}, deployed {process.Fingerprint})");

    /// <summary>
    /// The form of a fact a subscriber understands: the exact type or, for integration events, the same contract
    /// in the subscriber's version — upcast from an older one, unless a closer version is also published.
    /// </summary>
    private IEvent? Accept(IEvent @event, Type wanted)
    {
        if (@event.GetType() == wanted)
        {
            return @event;
        }

        if (Contract.Find(wanted) is not { } target
            || Contract.Find(@event.GetType()) is not { } source
            || source.Name != target.Name
            || source.Version > target.Version
            || registry.PublishedVersions(target.Name).Any(v => v > source.Version && v <= target.Version))
        {
            return null;
        }

        IIntegrationEvent current = (IIntegrationEvent)@event;
        for (var version = source.Version; version < target.Version; version++)
        {
            var upcaster = registry.Upcaster(target.Name, version) ?? throw new MissingUpcasterException(
                $"A subscriber expects {target} but {target.Name} v{version} was published, and there is no upcaster from v{version}. Register one with AddUpcaster().");
            current = upcaster.Map(current);
        }

        log(External, null, $"upcast {@event.GetType().Name} -> {current.GetType().Name}");
        return current;
    }

    // ── Running a process ─────────────────────────────────────────────────────────────────────

    private async ValueTask<StoredInstance> AdvanceAsync(
        Process process, StoredInstance instance, IEvent trigger, Changes changes, CancellationToken cancellationToken)
    {
        while (instance.Position < process.Nodes.Length)
        {
            switch (process.Nodes[instance.Position])
            {
                case StepNode step:
                    return SendStep(instance, trigger, step.Action, instance.Position, compensating: false, changes);

                case AwaitNode wait:
                    var parked = changes.Park.FirstOrDefault(p => p.Process == instance.Process && p.Key == instance.Key && p.Step == instance.Position)
                        ?? await store.FindParkedAsync(instance.Process, instance.Key, instance.Position, cancellationToken).ConfigureAwait(false);
                    if (parked is not null && types.TypeOf(parked.Type) == wait.Event)
                    {
                        changes.Park.Remove(parked);
                        changes.Unpark.Add(parked);
                        log(instance.Process, instance.Key, $"<- {types.TypeOf(parked.Type).Name} (parked)");
                        instance = Record(instance, HistoryKind.Received, step: instance.Position, message: parked.AsFact())
                            with { Position = instance.Position + 1, Facts = instance.Facts.Add(parked.AsFact()) };
                        continue;
                    }

                    DateTimeOffset? deadline = wait.Timeout is { } timeout ? now() + timeout : null;
                    Guid? timer = null;
                    if (deadline is { } at)
                    {
                        timer = newId();
                        changes.Schedule.Add(new Scheduled(at, new Envelope(
                            timer.Value, MessageKind.Timeout, TimeoutType, "{}", new ProcessRef(instance.Process, instance.Key, instance.Position, false), Trace: Tracing.Capture(instance))));
                    }

                    log(instance.Process, instance.Key, wait.Timeout is { } t
                        ? $"awaiting {wait.Event.Name} (within {Durations.Format(t)})"
                        : $"awaiting {wait.Event.Name}");
                    Tracing.Event("wyrd.waiting", instance, instance.Position);
                    return instance with { Status = ProcessStatus.Awaiting, Deadline = deadline, Timer = timer };
            }
        }

        log(instance.Process, instance.Key, "completed");
        return Record(instance, HistoryKind.Completed) with { Status = ProcessStatus.Completed, Pending = null };
    }

    private StoredInstance SendStep(StoredInstance instance, IEvent trigger, StepRef step, int index, bool compensating, Changes changes)
    {
        var command = Command(step, instance, trigger);
        var (type, payload) = types.Serialize(command, step.Output ?? command.GetType());
        var id = newId();
        var fences = compensating && instance.Uncertain.TryGetValue(index, out var original) ? original : (Guid?)null;
        changes.Outbox.Add(new Envelope(id, MessageKind.Command, type, payload, new ProcessRef(instance.Process, instance.Key, index, compensating), Fences: fences, Trace: Tracing.Capture(instance)));
        log(instance.Process, instance.Key, $"-> {command.GetType().Name}");
        return Record(instance, HistoryKind.Sent, index, compensating, attempt: 1, message: new StoredMessage(type, payload))
            with { Status = ProcessStatus.Running, Pending = id, Attempt = 1 };
    }

    // ── Replies: advance, retry or compensate ─────────────────────────────────────────────────

    private async ValueTask HandleReplyAsync(Envelope envelope, CancellationToken cancellationToken)
    {
        var target = envelope.Process!;
        var reply = MessageTypes.DeserializeReply(envelope.Payload);
        var instance = await store.LoadInstanceAsync(target.Process, target.Key, cancellationToken).ConfigureAwait(false);
        if (instance is not null)
        {
            Tracing.Instance(instance);
        }

        Tracing.Outcome(reply.Outcome);

        // A reply belongs to the pending command. A fault must also belong to the current attempt: a redelivered
        // fault of an earlier attempt must not consume another retry. A success is welcome whenever it arrives.
        if (instance is null || instance.Pending != envelope.ReplyTo
            || (reply.Outcome == ReplyOutcome.Faulted && target.Attempt != instance.Attempt))
        {
            Tracing.Outcome("stale", reply.Outcome);
            log(target.Process, target.Key, "stale reply ignored");
            return;
        }

        var process = registry.ProcessNamed(target.Process);
        var trigger = Trigger(instance);
        var changes = new Changes();

        // The instance keeps the reply for the operator: the owner's inbox forgets it after its deduplication retention,
        // and an instance can stay suspended longer than that.
        if (instance.Status != ProcessStatus.Suspended && DefinitionChanged(process, instance) is { } changed)
        {
            changes.Save(KeepReply(changed, envelope, reply));
            await store.CommitAsync(changes, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (instance.Fingerprint != process.Fingerprint)
        {
            log(target.Process, target.Key, "reply kept for later: the definition changed");
            changes.Save(KeepReply(instance, envelope, reply));
            await store.CommitAsync(changes, cancellationToken).ConfigureAwait(false);
            return;
        }

        // Handled now: a reply kept earlier for this command is no longer the operator's to deliver.
        instance = Record(instance with { KeptReply = null }, HistoryKind.Replied, target.Step, target.Compensating, target.Attempt,
            reply.Type is null ? null : new StoredMessage(reply.Type, reply.Payload!), outcome: reply.Outcome);

        if (target.Step == process.Nodes.Length)
        {
            changes.Save(Conclude(instance, process.Outcome!, target, trigger, reply, changes));
            await store.CommitAsync(changes, cancellationToken).ConfigureAwait(false);
            return;
        }

        var step = (StepNode)process.Nodes[target.Step];

        switch (reply.Outcome)
        {
            case ReplyOutcome.Accepted:
                log(instance.Process, instance.Key, $"ok {types.TypeOf(reply.Type!).Name}");
                instance = target.Compensating
                    ? ContinueCompensation(process, instance with { Completed = instance.Completed.RemoveAt(instance.Completed.Count - 1) }, trigger, changes)
                    : await AdvanceAsync(process, instance with
                    {
                        Completed = instance.Completed.Add(target.Step),
                        Position = target.Step + 1,
                        Pending = null,
                        Facts = instance.Facts.Add(new StoredMessage(reply.Type!, reply.Payload!)),
                    }, trigger, changes, cancellationToken).ConfigureAwait(false);
                break;

            case ReplyOutcome.NothingToUndo:
                // The owner fenced the original command: it never ran and never will. Proof, not a guess.
                log(instance.Process, instance.Key, $"nothing to undo: {step.Action} never ran");
                instance = ContinueCompensation(process, instance with { Completed = instance.Completed.RemoveAt(instance.Completed.Count - 1) }, trigger, changes);
                break;

            case ReplyOutcome.Rejected:
                log(instance.Process, instance.Key, $"rejected {types.Deserialize(reply.Type!, reply.Payload!)}");
                instance = target.Compensating
                    ? Suspend(instance, $"undo {step.Compensation} was rejected: {types.Deserialize(reply.Type!, reply.Payload!)}")
                    : StartCompensation(process, instance, trigger, changes);
                break;

            default:
                instance = RetryOrGiveUp(process, instance, step, target, trigger, changes);
                break;
        }

        changes.Save(instance);
        await store.CommitAsync(changes, cancellationToken).ConfigureAwait(false);
    }

    private StoredInstance RetryOrGiveUp(
        Process process, StoredInstance instance, StepNode step, ProcessRef target, IEvent trigger, Changes changes)
    {
        var retried = target.Compensating ? step.Compensation! : step.Action;
        var policy = target.Compensating ? step.UndoPolicy : step.Retry;
        if (Retry(instance, retried, policy, target, trigger, changes) is { } retrying)
        {
            return retrying;
        }

        if (target.Compensating)
        {
            return Suspend(instance, $"undo {retried} failed after {Attempts(instance.Attempt)}");
        }

        if (!step.CompensateWhenExhausted)
        {
            return Suspend(instance, $"{retried} failed after {Attempts(instance.Attempt)}");
        }

        return CompensateUncertain(process, instance, step, target.Step, trigger, changes);
    }

    /// <summary>
    /// Compensates a process that gave up on a step without knowing whether it ran: a delayed copy may still execute,
    /// so the step joins the undo list and its undo fences the original command.
    /// </summary>
    private StoredInstance CompensateUncertain(Process process, StoredInstance instance, StepNode step, int index, IEvent trigger, Changes changes)
    {
        if (step.Compensation is not null)
        {
            log(instance.Process, instance.Key, $"outcome of {step.Action} unknown: its undo will fence it");
            instance = instance with
            {
                Completed = instance.Completed.Add(index),
                Uncertain = instance.Uncertain.SetItem(index, instance.Pending!.Value),
            };
        }

        return StartCompensation(process, instance, trigger, changes);
    }

    /// <summary>
    /// Compensates a process whose pending step's reply was kept while its definition was changed: the outcome is known,
    /// so an accepted step is undone like any completed one, and a rejected one has nothing to undo. No fence needed.
    /// </summary>
    private StoredInstance CompensateKnown(Process process, StoredInstance instance, Envelope kept, IEvent trigger, Changes changes)
    {
        var target = kept.Process!;
        var reply = MessageTypes.DeserializeReply(kept.Payload);
        instance = Record(instance, HistoryKind.Replied, target.Step, target.Compensating, target.Attempt,
            reply.Type is null ? null : new StoredMessage(reply.Type, reply.Payload!), outcome: reply.Outcome);
        if (reply.Outcome == ReplyOutcome.Accepted)
        {
            instance = instance with
            {
                Completed = instance.Completed.Add(target.Step),
                Facts = instance.Facts.Add(new StoredMessage(reply.Type!, reply.Payload!)),
            };
        }

        return StartCompensation(process, instance, trigger, changes);
    }

    /// <summary>Schedules the same command again after the backoff, or returns null when no retry is left.</summary>
    private StoredInstance? Retry(StoredInstance instance, StepRef retried, RetryPolicy? policy, ProcessRef target, IEvent trigger, Changes changes)
    {
        var retries = policy?.Attempts.Count ?? 0;
        if (instance.Attempt > retries)
        {
            log(instance.Process, instance.Key, retries == 0 ? "transient failure, no retry policy" : "transient failure, retries exhausted");
            return null;
        }

        var delay = policy!.DelayBefore(instance.Attempt);
        log(instance.Process, instance.Key, $"transient failure, retry {instance.Attempt}/{retries} after {Durations.Format(delay)}");

        // Same message id: if an earlier attempt did go through, the owner's inbox replays its outcome.
        var command = Command(retried, instance, trigger);
        var (type, payload) = types.Serialize(command, retried.Output ?? command.GetType());
        var next = instance.Attempt + 1;
        Tracing.Event("wyrd.retry", instance, target.Step, next);
        var fences = target.Compensating && instance.Uncertain.TryGetValue(target.Step, out var original) ? original : (Guid?)null;
        changes.Schedule.Add(new Scheduled(now() + delay, new Envelope(
            instance.Pending!.Value, MessageKind.Command, type, payload, target with { Attempt = next }, Fences: fences, Trace: Tracing.Capture(instance))));
        return Record(instance, HistoryKind.Sent, target.Step, target.Compensating, next, new StoredMessage(type, payload), at: now() + delay)
            with { Attempt = next };
    }

    private StoredInstance StartCompensation(Process process, StoredInstance instance, IEvent trigger, Changes changes)
    {
        Tracing.Event("wyrd.compensating", instance);
        log(instance.Process, instance.Key, "compensating");
        return ContinueCompensation(process, instance with { Compensating = true, Status = ProcessStatus.Running, Pending = null }, trigger, changes);
    }

    private StoredInstance ContinueCompensation(Process process, StoredInstance instance, IEvent trigger, Changes changes)
    {
        var completed = instance.Completed;
        while (completed.Count > 0)
        {
            var step = (StepNode)process.Nodes[completed[^1]];
            if (step.Compensation is { } undo)
            {
                return SendStep(instance with { Completed = completed }, trigger, undo, completed[^1], compensating: true, changes);
            }

            completed = completed.RemoveAt(completed.Count - 1);
        }

        if (process.Outcome is { } outcome)
        {
            return SendStep(instance with { Completed = completed }, trigger, outcome.Action, process.Nodes.Length, compensating: true, changes);
        }

        log(instance.Process, instance.Key, "compensated");
        return Record(instance, HistoryKind.Compensated) with { Completed = completed, Status = ProcessStatus.Compensated, Pending = null };
    }

    /// <summary>The reply to the OnCompensated outcome command: the end of the compensation.</summary>
    private StoredInstance Conclude(
        StoredInstance instance, CompensatedNode outcome, ProcessRef target, IEvent trigger, ReplyMessage reply, Changes changes)
    {
        switch (reply.Outcome)
        {
            case ReplyOutcome.Accepted:
                log(instance.Process, instance.Key, $"ok {types.TypeOf(reply.Type!).Name}");
                log(instance.Process, instance.Key, "compensated");
                return Record(instance, HistoryKind.Compensated) with { Status = ProcessStatus.Compensated, Pending = null };
            case ReplyOutcome.Rejected:
                log(instance.Process, instance.Key, $"rejected {types.Deserialize(reply.Type!, reply.Payload!)}");
                return Suspend(instance, $"outcome {outcome.Action} was rejected: {types.Deserialize(reply.Type!, reply.Payload!)}");
            default:
                return Retry(instance, outcome.Action, outcome.Policy, target, trigger, changes)
                    ?? Suspend(instance, $"outcome {outcome.Action} failed after {Attempts(instance.Attempt)}");
        }
    }

    /// <summary>
    /// Stops instead of guessing. The instance keeps where it was — position, completed steps, pending command — so an
    /// operator can resume or compensate it; a late success of the pending command still resumes it on its own.
    /// </summary>
    private StoredInstance Suspend(StoredInstance instance, string reason)
    {
        Tracing.Event("wyrd.suspended", instance);
        log(instance.Process, instance.Key, $"SUSPENDED: {reason}, needs attention");
        return instance with
        {
            Status = ProcessStatus.Suspended,
            Reason = reason,
            History = instance.History.Add(new HistoryEntry(now(), HistoryKind.Suspended) { Reason = reason }),
        };
    }

    // ── Operators: resume or compensate a suspended instance ─────────────────────────────────

    /// <summary>
    /// Re-sends what the suspended instance was doing, with the same message id (the owner's inbox makes it safe), or
    /// delivers the reply it kept while its definition was changed.
    /// </summary>
    public async ValueTask ResumeAsync(string processName, string key, CancellationToken cancellationToken)
    {
        var (process, instance) = await SuspendedAsync(processName, key, cancellationToken).ConfigureAwait(false);
        using var operation = Tracing.Operator("resume", instance);
        Tracing.Instance(instance);
        var trigger = Trigger(instance);
        var changes = new Changes();
        log(instance.Process, instance.Key, "resumed by an operator");
        instance = Record(instance, HistoryKind.Resumed);

        var kept = KeptFor(instance);
        instance = instance with { Status = ProcessStatus.Running, Reason = null, KeptReply = null };
        if (kept is not null)
        {
            // The reply arrived while the definition was changed: delivered again, instead of asking the owner again.
            changes.Outbox.Add(kept with { MessageId = newId() });
        }
        else if (!instance.Compensating && process.Nodes[instance.Position] is AwaitNode)
        {
            // Waits again, with a fresh time limit; an event that arrived meanwhile was parked and is taken now.
            instance = await AdvanceAsync(process, instance, trigger, changes, cancellationToken).ConfigureAwait(false);
        }
        else if (!instance.Compensating)
        {
            var step = (StepNode)process.Nodes[instance.Position];
            instance = Resend(instance, trigger, step.Action, instance.Position, compensating: false, changes);
        }
        else if (instance.Completed.Count > 0)
        {
            var index = instance.Completed[^1];
            instance = Resend(instance, trigger, ((StepNode)process.Nodes[index]).Compensation!, index, compensating: true, changes);
        }
        else
        {
            instance = Resend(instance, trigger, process.Outcome!.Action, process.Nodes.Length, compensating: true, changes);
        }

        changes.Save(instance);
        operation?.SetTag("wyrd.process.status", instance.Status.ToString());
        await store.CommitAsync(changes, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Undoes a suspended instance that was going forward; the step it gave up on is fenced, like any unknown outcome.</summary>
    public async ValueTask CompensateAsync(string processName, string key, CancellationToken cancellationToken)
    {
        var (process, instance) = await SuspendedAsync(processName, key, cancellationToken).ConfigureAwait(false);
        using var operation = Tracing.Operator("compensate", instance);
        if (instance.Compensating)
        {
            throw new InstanceStateException(processName, key,
                $"{processName} '{key}' was already compensating when it was suspended: resume it to finish the compensation.");
        }

        Tracing.Instance(instance);
        var changes = new Changes();
        log(instance.Process, instance.Key, "compensation requested by an operator");
        instance = Record(instance, HistoryKind.CompensationRequested);
        var kept = KeptFor(instance);
        instance = instance with { Status = ProcessStatus.Running, Reason = null, Deadline = null, KeptReply = null };
        changes.Save(kept is not null
            ? CompensateKnown(process, instance, kept, Trigger(instance), changes)
            : process.Nodes[instance.Position] is StepNode step
                ? CompensateUncertain(process, instance, step, instance.Position, Trigger(instance), changes)
                : StartCompensation(process, instance, Trigger(instance), changes));
        operation?.SetTag("wyrd.process.status", changes.Find(processName, key)?.Status.ToString());
        await store.CommitAsync(changes, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The kept reply, when it answers the command the instance is waiting for; any other is stale.</summary>
    private static Envelope? KeptFor(StoredInstance instance) =>
        instance.KeptReply is { } kept && kept.ReplyTo == instance.Pending ? kept : null;

    private async ValueTask<(Process Process, StoredInstance Instance)> SuspendedAsync(string processName, string key, CancellationToken cancellationToken)
    {
        var instance = await store.LoadInstanceAsync(processName, key, cancellationToken).ConfigureAwait(false);
        if (instance is not { Status: ProcessStatus.Suspended })
        {
            throw new InstanceStateException(processName, key, $"{processName} '{key}' is not suspended (it is {instance?.Status.ToString() ?? "unknown"}).");
        }

        var process = registry.ProcessNamed(processName);
        return instance.Fingerprint == process.Fingerprint
            ? (process, instance)
            : throw new InstanceStateException(processName, key,
                $"{processName} '{key}' started with another definition ({instance.Fingerprint}; deployed: {process.Fingerprint}). "
                + "Deploy the definition it started with to resume or compensate it; migrating instances comes with process versioning.");
    }

    private StoredInstance Resend(StoredInstance instance, IEvent trigger, StepRef step, int index, bool compensating, Changes changes)
    {
        var command = Command(step, instance, trigger);
        var (type, payload) = types.Serialize(command, step.Output ?? command.GetType());
        var id = instance.Pending ?? newId();
        var fences = compensating && instance.Uncertain.TryGetValue(index, out var original) ? original : (Guid?)null;
        changes.Outbox.Add(new Envelope(id, MessageKind.Command, type, payload, new ProcessRef(instance.Process, instance.Key, index, compensating), Fences: fences, Trace: Tracing.Capture(instance)));
        log(instance.Process, instance.Key, $"-> {command.GetType().Name} (again)");
        return Record(instance, HistoryKind.Sent, index, compensating, attempt: 1, message: new StoredMessage(type, payload))
            with { Pending = id, Attempt = 1 };
    }

    // ── Timeouts ──────────────────────────────────────────────────────────────────────────────

    private async ValueTask HandleTimeoutAsync(Envelope envelope, CancellationToken cancellationToken)
    {
        var target = envelope.Process!;
        var instance = await store.LoadInstanceAsync(target.Process, target.Key, cancellationToken).ConfigureAwait(false);
        if (instance is not null)
        {
            Tracing.Instance(instance);
        }
        if (instance is not { Status: ProcessStatus.Awaiting } || instance.Position != target.Step)
        {
            Tracing.Outcome("stale");
            return; // The wait is over already.
        }

        var process = registry.ProcessNamed(target.Process);
        var changes = new Changes();
        if (DefinitionChanged(process, instance) is { } changed)
        {
            changes.Save(changed);
            await store.CommitAsync(changes, cancellationToken).ConfigureAwait(false);
            return;
        }

        Tracing.Outcome("timeout");
        var wait = (AwaitNode)process.Nodes[target.Step];
        log(instance.Process, instance.Key, $"timeout: {wait.Event.Name} not received in time");
        instance = Record(instance, HistoryKind.TimedOut, step: target.Step);
        changes.Save(StartCompensation(process, instance with { Deadline = null, Timer = null }, Trigger(instance), changes));
        await store.CommitAsync(changes, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Calls a step with the trigger and the facts it reads: the latest event of each type it asks for. The registry
    /// checked that an earlier step produces every one of them, so they are there.
    /// </summary>
    private ICommand Command(StepRef step, StoredInstance instance, IEvent trigger) =>
        step.Invoke!(trigger, wanted =>
        {
            for (var i = instance.Facts.Count - 1; i >= 0; i--)
            {
                if (wanted.IsAssignableFrom(types.TypeOf(instance.Facts[i].Type)))
                {
                    return types.Deserialize(instance.Facts[i].Type, instance.Facts[i].Payload);
                }
            }

            throw new InvalidOperationException($"Step '{step}' reads {wanted.Name}, but {instance.Process} '{instance.Key}' has no such event.");
        });

    /// <summary>Appends what crossed the instance's boundary to its history (Urd).</summary>
    private StoredInstance Record(
        StoredInstance instance, HistoryKind kind, int? step = null, bool compensating = false, int? attempt = null,
        StoredMessage? message = null, DateTimeOffset? at = null, ReplyOutcome? outcome = null)
    {
        if (Tracing.IsEnabled && kind is (HistoryKind.Completed or HistoryKind.Compensated or HistoryKind.Resumed or HistoryKind.CompensationRequested
            or HistoryKind.Received or HistoryKind.TimedOut))
        {
            var name = kind switch
            {
                HistoryKind.Completed => "wyrd.completed",
                HistoryKind.Compensated => "wyrd.compensated",
                HistoryKind.Resumed => "wyrd.resumed",
                HistoryKind.CompensationRequested => "wyrd.compensation_requested",
                HistoryKind.Received => "wyrd.received",
                _ => "wyrd.timed_out",
            };
            Tracing.Event(name, instance, step, attempt);
        }

        return instance with
        {
            History = instance.History.Add(new HistoryEntry(at ?? now(), kind)
            {
                Type = message?.Type,
                Payload = message?.Payload,
                Step = step,
                Compensating = compensating,
                Attempt = attempt,
                Outcome = outcome?.ToString(),
            }),
        };
    }

    private static string Attempts(int count) => count == 1 ? "1 attempt" : $"{count} attempts";

    private IEvent Trigger(StoredInstance instance) => (IEvent)types.Deserialize(instance.TriggerType, instance.TriggerPayload);
}
