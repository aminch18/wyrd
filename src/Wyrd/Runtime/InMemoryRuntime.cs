using System.Diagnostics.CodeAnalysis;
using Wyrd.Processes;

namespace Wyrd.Runtime;

/// <summary>
/// Runs Wyrd in a single process with an in-memory queue, store and virtual clock. It behaves like the
/// distributed runtime — messages, replies, JSON, outbox, inbox, durable timers — but deterministically:
/// same inputs, same journal, every time. Use it for local development, tests, simulation and replay.
/// </summary>
public sealed class InMemoryRuntime
{
    private static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly Registry _registry = new();
    private readonly InMemoryStore _store = new();
    private readonly MessageTypes _types = new();
    private readonly Queue<Envelope> _queue = new();
    private readonly Dictionary<Type, int> _faults = [];
    private readonly List<JournalEntry> _journal = [];
    private readonly Engine _engine;
    private DateTimeOffset _now = Epoch;
    private long _ids;

    [RequiresUnreferencedCode("Messages and states are serialized with reflection-based System.Text.Json.")]
    [RequiresDynamicCode("Messages and states are serialized with reflection-based System.Text.Json.")]
    public InMemoryRuntime() =>
        _engine = new Engine(_registry, _store, _types, () => _now, NextId, _faults, Log);

    /// <summary>Virtual time elapsed since the runtime started.</summary>
    public TimeSpan Now => _now - Epoch;

    public IReadOnlyList<JournalEntry> Journal => _journal;

    /// <summary>Every accepted domain event, in order (as stored, after a JSON round trip).</summary>
    public IReadOnlyList<IDomainEvent> History => [.. _store.Events.Select(e => (IDomainEvent)_types.Deserialize(e.Type, e.Payload))];

    /// <summary>Every integration event published to other contexts, in order.</summary>
    public IReadOnlyList<IIntegrationEvent> Published => [.. _store.Published.Select(e => (IIntegrationEvent)_types.Deserialize(e.Type, e.Payload))];

    /// <summary>Registers the single owner of a command type.</summary>
    public InMemoryRuntime AddSlice<TCommand, TState, TEvent, TError>(Slice<TCommand, TState, TEvent, TError> slice)
        where TCommand : ICommand
        where TState : IState<TState>
        where TEvent : IDomainEvent
        where TError : notnull
    {
        ArgumentNullException.ThrowIfNull(slice);
        _registry.Add(slice);
        return this;
    }

    /// <summary>Registers a mapping from a domain event to an integration event.</summary>
    public InMemoryRuntime AddPublication<TDomainEvent, TIntegrationEvent>(Publication<TDomainEvent, TIntegrationEvent> publication)
        where TDomainEvent : IDomainEvent
        where TIntegrationEvent : IIntegrationEvent
    {
        ArgumentNullException.ThrowIfNull(publication);
        _types.NameOf(typeof(TIntegrationEvent));
        _registry.Add(publication);
        return this;
    }

    /// <summary>Registers an upcaster from one contract version to the next.</summary>
    public InMemoryRuntime AddUpcaster<TFrom, TTo>(Upcaster<TFrom, TTo> upcaster)
        where TFrom : IIntegrationEvent
        where TTo : IIntegrationEvent
    {
        ArgumentNullException.ThrowIfNull(upcaster);
        _types.NameOf(typeof(TFrom));
        _types.NameOf(typeof(TTo));
        _registry.Add(upcaster);
        return this;
    }

    public InMemoryRuntime AddProcess(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        _registry.Add(process);
        _types.NameOf(process.Trigger);
        foreach (var wait in process.Nodes.OfType<AwaitNode>())
        {
            _types.NameOf(wait.Event);
        }

        return this;
    }

    /// <summary>
    /// Executes a command coming from outside a process (an API call, a UI action...), returns the decision
    /// and then lets everything it caused run until the system is idle.
    /// </summary>
    public async Task<Decision> SendAsync(ICommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var key = Keys.Of(_registry.OwnerOf(command.GetType()).KeyOf(command));
        Log(Engine.External, key, $"-> {command.GetType().Name}");

        var (type, payload) = _types.Serialize(command);
        var decision = (await _engine.HandleAsync(new Envelope(NextId(), MessageKind.Command, type, payload), cancellationToken).ConfigureAwait(false))!;
        Log(Engine.External, key, decision switch
        {
            Decision.Accepted accepted => $"ok {accepted.Event.GetType().Name}",
            Decision.Rejected rejected => $"rejected {rejected.Error}",
            _ => throw new InvalidOperationException($"Unknown decision {decision}."),
        });

        await DrainAsync(cancellationToken).ConfigureAwait(false);
        return decision;
    }

    /// <summary>
    /// Delivers a fact from another context (a webhook, a message from another service...).
    /// Only integration events can come from outside: domain events are produced by slices alone.
    /// </summary>
    public async Task PublishAsync(IIntegrationEvent @event, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);
        var (type, payload) = _types.Serialize(@event);
        _queue.Enqueue(new Envelope(NextId(), MessageKind.Event, type, payload));
        await DrainAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Moves the virtual clock forward, delivering every timer and scheduled retry that falls due, in order.</summary>
    public async Task AdvanceAsync(TimeSpan by, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(by, TimeSpan.Zero);
        var until = _now + by;
        while (await _store.TakeNextDueAsync(until, cancellationToken).ConfigureAwait(false) is { } due)
        {
            _now = due.At > _now ? due.At : _now;
            _queue.Enqueue(due.Envelope);
            await DrainAsync(cancellationToken).ConfigureAwait(false);
        }

        _now = until;
    }

    /// <summary>Makes the next <paramref name="times"/> executions of a command, sent by a process, fail transiently.</summary>
    public void FailNext<TCommand>(int times = 1)
        where TCommand : ICommand
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(times, 1);
        _faults[typeof(TCommand)] = _faults.GetValueOrDefault(typeof(TCommand)) + times;
    }

    public ProcessStatus? StatusOf(Process process, object key)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(key);
        return _store.Instance(process.Name, Keys.Of(key))?.Status;
    }

    public TState StateOf<TState>(object key)
        where TState : IState<TState>
    {
        ArgumentNullException.ThrowIfNull(key);
        return _store.State(typeof(TState).FullName!, Keys.Of(key)) is { } state
            ? (TState)_types.Deserialize(state.Type, state.Payload)
            : TState.Initial;
    }

    private async Task DrainAsync(CancellationToken cancellationToken)
    {
        await RelayAsync(cancellationToken).ConfigureAwait(false);
        while (_queue.TryDequeue(out var envelope))
        {
            await _engine.HandleAsync(envelope, cancellationToken).ConfigureAwait(false);
            await RelayAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>The outbox relay: committed messages are handed to the transport.</summary>
    private async Task RelayAsync(CancellationToken cancellationToken)
    {
        foreach (var envelope in await _store.TakeOutboxAsync(cancellationToken).ConfigureAwait(false))
        {
            _queue.Enqueue(envelope);
        }
    }

    // Deterministic message ids: same inputs, same ids.
    private Guid NextId()
    {
        Span<byte> bytes = stackalloc byte[16];
        BitConverter.TryWriteBytes(bytes, ++_ids);
        return new Guid(bytes);
    }

    private void Log(string process, string? key, string message) => _journal.Add(new(Now, process, key, message));
}
