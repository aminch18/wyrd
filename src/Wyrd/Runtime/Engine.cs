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
    Dictionary<Type, int> faults,
    Action<string, string?, string> log)
{
    public const string External = "-";
    private const string ReplyType = "wyrd.reply";
    private const string TimeoutType = "wyrd.timeout";

    public async ValueTask<Decision?> HandleAsync(Envelope envelope, CancellationToken cancellationToken)
    {
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

    // ── Commands: the owner decides ───────────────────────────────────────────────────────────

    private async ValueTask<Decision?> HandleCommandAsync(Envelope envelope, CancellationToken cancellationToken)
    {
        var command = (ICommand)types.Deserialize(envelope.Type, envelope.Payload);
        var slice = registry.OwnerOf(command.GetType());
        var key = Keys.Of(slice.KeyOf(command));
        var changes = new Changes();

        if (await store.FindReplyAsync(envelope.MessageId, cancellationToken).ConfigureAwait(false) is { } stored)
        {
            log(External, key, $"duplicate {command.GetType().Name} ignored, stored outcome replayed");
            Reply(envelope, stored, changes);
            await store.CommitAsync(changes, cancellationToken).ConfigureAwait(false);
            return null;
        }

        if (envelope.Process is not null && TryInjectFault(command.GetType()))
        {
            Reply(envelope, new ReplyMessage(ReplyOutcome.Faulted), changes);
            await store.CommitAsync(changes, cancellationToken).ConfigureAwait(false);
            return null;
        }

        var stateType = slice.State.FullName!;
        var current = await store.LoadStateAsync(stateType, key, cancellationToken).ConfigureAwait(false);
        var state = current is null ? slice.Initial : types.Deserialize(current.Type, current.Payload);
        var decision = slice.Decide(command, state);

        ReplyMessage reply;
        if (decision is Decision.Accepted accepted)
        {
            var (nextType, nextPayload) = types.Serialize(((IState)state).Apply(accepted.Event), slice.State);
            changes.States.Add(new StoredState(stateType, key, nextType, nextPayload, (current?.Version ?? 0) + 1));

            var (eventType, eventPayload) = types.Serialize(accepted.Event, slice.Event);
            changes.Events.Add(new StoredMessage(eventType, eventPayload));
            changes.Outbox.Add(new Envelope(newId(), MessageKind.Event, eventType, eventPayload));

            foreach (var publication in registry.PublicationsOf(accepted.Event.GetType()))
            {
                var published = publication.Map(accepted.Event);
                var (publishedType, publishedPayload) = types.Serialize(published, published.GetType());
                changes.Published.Add(new StoredMessage(publishedType, publishedPayload));
                changes.Outbox.Add(new Envelope(newId(), MessageKind.Event, publishedType, publishedPayload));
                log(External, key, $"published {types.TypeOf(publishedType).Name}");
            }

            reply = new ReplyMessage(ReplyOutcome.Accepted, eventType, eventPayload);
        }
        else
        {
            var (errorType, errorPayload) = types.Serialize(((Decision.Rejected)decision).Error, slice.Error);
            reply = new ReplyMessage(ReplyOutcome.Rejected, errorType, errorPayload);
        }

        changes.Replies[envelope.MessageId] = reply;
        Reply(envelope, reply, changes);
        await store.CommitAsync(changes, cancellationToken).ConfigureAwait(false);
        return decision;
    }

    private void Reply(Envelope command, ReplyMessage reply, Changes changes)
    {
        if (command.Process is not null)
        {
            changes.Outbox.Add(new Envelope(
                newId(), MessageKind.Reply, ReplyType, MessageTypes.SerializeReply(reply), command.Process, command.MessageId));
        }
    }

    private bool TryInjectFault(Type command)
    {
        if (faults.GetValueOrDefault(command) == 0)
        {
            return false;
        }

        faults[command]--;
        return true;
    }

    // ── Events: start processes, resume waits, park early arrivals ─────────────────────────────

    private async ValueTask HandleEventAsync(Envelope envelope, CancellationToken cancellationToken)
    {
        if (await store.WasHandledAsync(envelope.MessageId, cancellationToken).ConfigureAwait(false))
        {
            return; // Redelivery of a message we already handled.
        }

        var @event = (IEvent)types.Deserialize(envelope.Type, envelope.Payload);
        var changes = new Changes();
        changes.Handled.Add(envelope.MessageId);

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

        foreach (var process in registry.Processes)
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

    private async ValueTask ProjectAsync(IProjectionHandler projection, IEvent @event, Changes changes, CancellationToken cancellationToken)
    {
        var viewType = projection.View.FullName!;
        var key = Keys.Of(projection.KeyOf(@event));
        var stored = changes.Views.TryGetValue((viewType, key), out var pending)
            ? pending
            : await store.LoadViewAsync(viewType, key, cancellationToken).ConfigureAwait(false);
        var current = stored?.Payload is null ? null : types.Deserialize(stored.Type!, stored.Payload);

        var next = projection.Apply(current, @event);
        if (next is null)
        {
            changes.Views[(viewType, key)] = new StoredView(viewType, key, null, null);
            return;
        }

        var (type, payload) = types.Serialize(next, projection.View);
        changes.Views[(viewType, key)] = new StoredView(viewType, key, type, payload);
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
            process.Name, key, triggerType, triggerPayload, 0, ProcessStatus.Running, [], 0, null, false, null, 0);
        log(process.Name, key, $"started by {trigger.GetType().Name}");
        changes.Save(await AdvanceAsync(process, instance, trigger, changes, cancellationToken).ConfigureAwait(false));
    }

    private async ValueTask ResumeOrParkAsync(
        Process process, int step, AwaitNode wait, IEvent awaited, Changes changes, CancellationToken cancellationToken)
    {
        var key = Keys.Of(wait.KeyOf!(awaited));
        var instance = changes.Find(process.Name, key)
            ?? await store.LoadInstanceAsync(process.Name, key, cancellationToken).ConfigureAwait(false);

        if (instance is { Status: ProcessStatus.Awaiting } && instance.Position == step)
        {
            log(process.Name, key, $"<- {awaited.GetType().Name}");
            var trigger = Trigger(instance);
            instance = instance with { Position = step + 1, Status = ProcessStatus.Running, Deadline = null };
            changes.Save(await AdvanceAsync(process, instance, trigger, changes, cancellationToken).ConfigureAwait(false));
        }
        else if (instance is null || (instance is { Status: ProcessStatus.Running, Compensating: false } && instance.Position < step))
        {
            // Arrived before the process was waiting for it (common in distributed systems): keep it.
            log(process.Name, key, $"{awaited.GetType().Name} arrived early, parked");
            var (type, payload) = types.Serialize(awaited, wait.Event);
            changes.Park.Add(new StoredParked(process.Name, key, step, type, payload));
        }
    }

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
            var upcaster = registry.Upcaster(target.Name, version) ?? throw new InvalidOperationException(
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
                    if (parked is not null)
                    {
                        changes.Park.Remove(parked);
                        changes.Unpark.Add(parked);
                        log(instance.Process, instance.Key, $"<- {types.TypeOf(parked.Type).Name} (parked)");
                        instance = instance with { Position = instance.Position + 1 };
                        continue;
                    }

                    DateTimeOffset? deadline = wait.Timeout is { } timeout ? now() + timeout : null;
                    if (deadline is { } at)
                    {
                        changes.Schedule.Add(new Scheduled(at, new Envelope(
                            newId(), MessageKind.Timeout, TimeoutType, "{}", new ProcessRef(instance.Process, instance.Key, instance.Position, false))));
                    }

                    log(instance.Process, instance.Key, wait.Timeout is { } t
                        ? $"awaiting {wait.Event.Name} (within {Durations.Format(t)})"
                        : $"awaiting {wait.Event.Name}");
                    return instance with { Status = ProcessStatus.Awaiting, Deadline = deadline };
            }
        }

        log(instance.Process, instance.Key, "completed");
        return instance with { Status = ProcessStatus.Completed, Pending = null };
    }

    private StoredInstance SendStep(StoredInstance instance, IEvent trigger, StepRef step, int index, bool compensating, Changes changes)
    {
        var command = step.Invoke!(trigger);
        var (type, payload) = types.Serialize(command, step.Output ?? command.GetType());
        var id = newId();
        changes.Outbox.Add(new Envelope(id, MessageKind.Command, type, payload, new ProcessRef(instance.Process, instance.Key, index, compensating)));
        log(instance.Process, instance.Key, $"-> {command.GetType().Name}");
        return instance with { Status = ProcessStatus.Running, Pending = id, Attempt = 1 };
    }

    // ── Replies: advance, retry or compensate ─────────────────────────────────────────────────

    private async ValueTask HandleReplyAsync(Envelope envelope, CancellationToken cancellationToken)
    {
        var target = envelope.Process!;
        var instance = await store.LoadInstanceAsync(target.Process, target.Key, cancellationToken).ConfigureAwait(false);
        if (instance is null || instance.Pending != envelope.ReplyTo)
        {
            log(target.Process, target.Key, "stale reply ignored");
            return;
        }

        var process = registry.ProcessNamed(target.Process);
        var reply = MessageTypes.DeserializeReply(envelope.Payload);
        var trigger = Trigger(instance);
        var changes = new Changes();

        if (target.Step == process.Nodes.Length)
        {
            changes.Save(Conclude(instance, process.OnCompensated!, reply));
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
                    : await AdvanceAsync(process, instance with { Completed = instance.Completed.Add(target.Step), Position = target.Step + 1, Pending = null }, trigger, changes, cancellationToken).ConfigureAwait(false);
                break;

            case ReplyOutcome.Rejected:
                log(instance.Process, instance.Key, $"rejected {types.Deserialize(reply.Type!, reply.Payload!)}");
                instance = target.Compensating
                    ? Fail(instance, step.Compensation!)
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
        var retries = step.Retry?.Attempts.Count ?? 0;
        if (instance.Attempt <= retries)
        {
            var delay = step.Retry!.DelayBefore(instance.Attempt);
            log(instance.Process, instance.Key, $"transient failure, retry {instance.Attempt}/{retries} after {Durations.Format(delay)}");

            // Same message id: if the first attempt did go through, the owner's inbox replays its outcome.
            var retried = target.Compensating ? step.Compensation! : step.Action;
            var command = retried.Invoke!(trigger);
            var (type, payload) = types.Serialize(command, retried.Output ?? command.GetType());
            changes.Schedule.Add(new Scheduled(now() + delay, new Envelope(instance.Pending!.Value, MessageKind.Command, type, payload, target)));
            return instance with { Attempt = instance.Attempt + 1 };
        }

        log(instance.Process, instance.Key, retries == 0 ? "transient failure, no retry policy" : "transient failure, retries exhausted");
        return target.Compensating ? Fail(instance, step.Compensation!) : StartCompensation(process, instance, trigger, changes);
    }

    private StoredInstance StartCompensation(Process process, StoredInstance instance, IEvent trigger, Changes changes)
    {
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

        if (process.OnCompensated is { } outcome)
        {
            return SendStep(instance with { Completed = completed }, trigger, outcome, process.Nodes.Length, compensating: true, changes);
        }

        log(instance.Process, instance.Key, "compensated");
        return instance with { Completed = completed, Status = ProcessStatus.Compensated, Pending = null };
    }

    /// <summary>The reply to the OnCompensated outcome command: the end of the compensation.</summary>
    private StoredInstance Conclude(StoredInstance instance, StepRef outcome, ReplyMessage reply)
    {
        switch (reply.Outcome)
        {
            case ReplyOutcome.Accepted:
                log(instance.Process, instance.Key, $"ok {types.TypeOf(reply.Type!).Name}");
                log(instance.Process, instance.Key, "compensated");
                return instance with { Status = ProcessStatus.Compensated, Pending = null };
            case ReplyOutcome.Rejected:
                log(instance.Process, instance.Key, $"rejected {types.Deserialize(reply.Type!, reply.Payload!)}");
                return Fail(instance, outcome);
            default:
                log(instance.Process, instance.Key, "transient failure, no retry policy");
                return Fail(instance, outcome);
        }
    }

    private StoredInstance Fail(StoredInstance instance, StepRef undo)
    {
        log(instance.Process, instance.Key, $"FAILED: {undo} could not run, needs attention");
        return instance with { Status = ProcessStatus.Failed, Pending = null };
    }

    // ── Timeouts ──────────────────────────────────────────────────────────────────────────────

    private async ValueTask HandleTimeoutAsync(Envelope envelope, CancellationToken cancellationToken)
    {
        var target = envelope.Process!;
        var instance = await store.LoadInstanceAsync(target.Process, target.Key, cancellationToken).ConfigureAwait(false);
        if (instance is not { Status: ProcessStatus.Awaiting } || instance.Position != target.Step)
        {
            return; // The wait is over already.
        }

        var process = registry.ProcessNamed(target.Process);
        var wait = (AwaitNode)process.Nodes[target.Step];
        var changes = new Changes();
        log(instance.Process, instance.Key, $"timeout: {wait.Event.Name} not received in time");
        changes.Save(StartCompensation(process, instance with { Deadline = null }, Trigger(instance), changes));
        await store.CommitAsync(changes, cancellationToken).ConfigureAwait(false);
    }

    private IEvent Trigger(StoredInstance instance) => (IEvent)types.Deserialize(instance.TriggerType, instance.TriggerPayload);
}
