using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Wyrd.Processes;

namespace Wyrd.Runtime;

/// <summary>
/// Runs Wyrd in a single process with an in-memory queue, store and virtual clock. It behaves like the
/// distributed runtime — messages, replies, JSON, outbox, inbox, durable timers — but deterministically:
/// same inputs, same journal, every time. Use it for local development, tests, simulation and replay.
/// </summary>
public sealed class InMemoryRuntime : IWyrdBuilder<InMemoryRuntime>, IWyrdRuntime, IDisposable
{
    private static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly Application _application;
    private readonly InMemoryStore _store = new();
    private readonly MessageTypes _types;
    private readonly List<Envelope> _queue = [];
    private readonly Dictionary<Type, int> _faults = [];
    private readonly List<JournalEntry> _journal = [];
    private readonly Engine _engine;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _now = Epoch;
    private IWyrdTransport? _transport;
    private int _relayed;
    private long _ids;

    /// <summary>Reflection-based JSON: convenient for development and tests. Use a <c>[WyrdJsonContext]</c> for AOT.</summary>
    [RequiresUnreferencedCode("Reflection-based System.Text.Json. Pass a [WyrdJsonContext] for AOT and trimming.")]
    [RequiresDynamicCode("Reflection-based System.Text.Json. Pass a [WyrdJsonContext] for AOT and trimming.")]
    public InMemoryRuntime()
        : this(MessageTypes.Reflection())
    {
    }

    /// <summary>Source-generated JSON from your <c>[WyrdJsonContext]</c>: no reflection, AOT-safe.</summary>
    public InMemoryRuntime(JsonSerializerContext json)
        : this(MessageTypes.FromContext(json ?? throw new ArgumentNullException(nameof(json))))
    {
    }

    private InMemoryRuntime(MessageTypes types)
    {
        _application = new Application(types);
        _types = types;
        _engine = new Engine(_application.Registry, _store, _types, () => _now, NextId, Fault, Log);
    }

    /// <summary>Virtual time elapsed since the runtime started.</summary>
    public TimeSpan Now => _now - Epoch;

    public IReadOnlyList<JournalEntry> Journal => Read(() => (IReadOnlyList<JournalEntry>)[.. _journal]);

    /// <summary>Every accepted domain event, in order (as stored, after a JSON round trip).</summary>
    public IReadOnlyList<IDomainEvent> History =>
        Read(() => (IReadOnlyList<IDomainEvent>)[.. _store.Events.Select(e => (IDomainEvent)_types.Deserialize(e.Type, e.Payload))]);

    /// <summary>Every integration event published to other contexts, in order.</summary>
    public IReadOnlyList<IIntegrationEvent> Published =>
        Read(() => (IReadOnlyList<IIntegrationEvent>)[.. _store.Published.Select(e => (IIntegrationEvent)_types.Deserialize(e.Type, e.Payload))]);

    /// <summary>Registers the single owner of a command type.</summary>
    /// <summary>Registers the integration that owns a command carried out by another system (the only I/O).</summary>
    public InMemoryRuntime AddIntegration<TCommand, TEvent, TError>(Integration<TCommand, TEvent, TError> integration)
        where TCommand : ICommand
        where TEvent : IDomainEvent
        where TError : notnull
    {
        ArgumentNullException.ThrowIfNull(integration);
        _application.Add(integration);
        return this;
    }

    public InMemoryRuntime AddSlice<TCommand, TState, TEvent, TError>(Slice<TCommand, TState, TEvent, TError> slice)
        where TCommand : ICommand
        where TState : IState<TState>
        where TEvent : IDomainEvent
        where TError : notnull
    {
        ArgumentNullException.ThrowIfNull(slice);
        _application.Add(slice);
        return this;
    }

    /// <summary>Registers a mapping from a domain event to an integration event.</summary>
    public InMemoryRuntime AddPublication<TDomainEvent, TIntegrationEvent>(Publication<TDomainEvent, TIntegrationEvent> publication)
        where TDomainEvent : IDomainEvent
        where TIntegrationEvent : IIntegrationEvent
    {
        ArgumentNullException.ThrowIfNull(publication);
        _application.Add(publication);
        return this;
    }

    /// <summary>Registers an upcaster from one contract version to the next.</summary>
    public InMemoryRuntime AddUpcaster<TFrom, TTo>(Upcaster<TFrom, TTo> upcaster)
        where TFrom : IIntegrationEvent
        where TTo : IIntegrationEvent
    {
        ArgumentNullException.ThrowIfNull(upcaster);
        _application.Add(upcaster);
        return this;
    }

    /// <summary>Registers the projection that owns a read model.</summary>
    public InMemoryRuntime AddProjection<TView>(Projection<TView> projection)
        where TView : class
    {
        ArgumentNullException.ThrowIfNull(projection);
        _application.Add(projection);
        return this;
    }

    /// <summary>Replaces a process with a new definition of the same name, keeping its instances: a redeploy.</summary>
    internal void Redeploy(Process process) => _application.Redeploy(process);

    public InMemoryRuntime AddProcess(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        _application.Add(process);
        return this;
    }

    /// <summary>
    /// Executes a command coming from outside a process (an API call, a UI action...), returns the decision
    /// and then lets everything it caused run until the system is idle.
    /// </summary>
    public async Task<Decision> SendAsync(ICommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        Decision decision;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            decision = await SendCoreAsync(command, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        await RelayAsync(cancellationToken).ConfigureAwait(false);
        return decision;
    }

    private async Task<Decision> SendCoreAsync(ICommand command, CancellationToken cancellationToken)
    {
        var key = Keys.Of(_application.Registry.OwnerOf(command.GetType()).KeyOf(command));
        Log(Engine.External, key, $"-> {command.GetType().Name}");

        var (type, payload) = _types.Serialize(command, command.GetType());
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
    public async Task PublishAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var (type, payload) = _types.Serialize(integrationEvent, integrationEvent.GetType());
            _queue.Add(new Envelope(NextId(), MessageKind.Event, type, payload));
            await DrainAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        await RelayAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Moves the virtual clock forward, delivering every timer and scheduled retry that falls due, in order.</summary>
    public async Task AdvanceAsync(TimeSpan by, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(by, TimeSpan.Zero);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var until = _now + by;
            while (await _store.TakeNextDueAsync(until, cancellationToken).ConfigureAwait(false) is { } due)
            {
                _now = due.At > _now ? due.At : _now;
                _queue.Add(due.Envelope);
                await DrainAsync(cancellationToken).ConfigureAwait(false);
            }

            _now = until;
        }
        finally
        {
            _gate.Release();
        }

        await RelayAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Resumes a suspended instance (after fixing what made it stop): what it was doing is sent again with the same
    /// message id, so a command that did run in the meantime is not run twice.
    /// </summary>
    public Task ResumeAsync(Process process, object key, CancellationToken cancellationToken = default) =>
        OperateAsync(process, key, (name, k, ct) => _engine.ResumeAsync(name, k, ct), cancellationToken);

    /// <summary>Compensates a suspended instance that was going forward: what it completed is undone, and the step it gave up on is fenced.</summary>
    public Task CompensateAsync(Process process, object key, CancellationToken cancellationToken = default) =>
        OperateAsync(process, key, (name, k, ct) => _engine.CompensateAsync(name, k, ct), cancellationToken);

    /// <summary>
    /// Everything the instance received and sent, in order (Urd): export it with <see cref="InstanceHistory.ToJson"/>
    /// and replay it as a test with <c>Urd.ReplayAsync</c>.
    /// </summary>
    public InstanceHistory HistoryOf(Process process, object key)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(key);
        return Read(() => _store.Instance(process.Name, Keys.Of(key)) is { } instance
            ? new InstanceHistory(instance.Process, instance.Key, instance.Fingerprint, instance.History)
            : throw new InvalidOperationException($"{process.Name} has no instance '{Keys.Of(key)}'."));
    }

    /// <summary>The reason an instance is suspended, or null.</summary>
    public string? ReasonOf(Process process, object key)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(key);
        return Read(() => _store.Instance(process.Name, Keys.Of(key))?.Reason);
    }

    private async Task OperateAsync(Process process, object key, Func<string, string, CancellationToken, ValueTask> operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(key);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await operation(process.Name, Keys.Of(key), cancellationToken).ConfigureAwait(false);
            await DrainAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        await RelayAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Makes the next <paramref name="times"/> executions of a command, sent by a process, fail transiently.</summary>
    public void FailNext<TCommand>(int times = 1)
        where TCommand : ICommand
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(times, 1);
        Read(() => _faults[typeof(TCommand)] = _faults.GetValueOrDefault(typeof(TCommand)) + times);
    }

    public ProcessStatus? StatusOf(Process process, object key)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(key);
        return Read(() => _store.Instance(process.Name, Keys.Of(key))?.Status);
    }

    public TState StateOf<TState>(object key)
        where TState : IState<TState>
    {
        ArgumentNullException.ThrowIfNull(key);
        return Read(() => _store.State(typeof(TState).FullName!, Keys.Of(key)) is { } state
            ? (TState)_types.Deserialize(state.Type, state.Payload)
            : TState.Initial);
    }

    Task<ProcessStatus?> IWyrdRuntime.StatusOfAsync(Process process, object key, CancellationToken cancellationToken) =>
        Task.FromResult(StatusOf(process, key));

    Task<InstanceHistory?> IWyrdRuntime.HistoryOfAsync(Process process, object key, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(key);
        return Task.FromResult(Read(() => _store.Instance(process.Name, Keys.Of(key)) is { } instance
            ? new InstanceHistory(instance.Process, instance.Key, instance.Fingerprint, instance.History)
            : null));
    }

    Task<TState> IWyrdRuntime.StateOfAsync<TState>(object key, CancellationToken cancellationToken) =>
        Task.FromResult(StateOf<TState>(key));

    Task<TView?> IWyrdRuntime.ViewAsync<TView>(object key, CancellationToken cancellationToken)
        where TView : class =>
        Task.FromResult(View<TView>(key));

    Task<IReadOnlyList<TView>> IWyrdRuntime.ViewsAsync<TView>(CancellationToken cancellationToken) =>
        Task.FromResult(Views<TView>());

    /// <summary>Reads one document of a read model, or null.</summary>
    public TView? View<TView>(object key)
        where TView : class
    {
        ArgumentNullException.ThrowIfNull(key);
        return Read(() => _store.View(typeof(TView).FullName!, Keys.Of(key)) is { Payload: { } payload, Type: { } type }
            ? (TView)_types.Deserialize(type, payload)
            : null);
    }

    /// <summary>Reads every document of a read model, in creation order.</summary>
    public IReadOnlyList<TView> Views<TView>()
        where TView : class =>
        Read(() => (IReadOnlyList<TView>)[.. _store.Views(typeof(TView).FullName!).Select(v => (TView)_types.Deserialize(v.Type!, v.Payload!))]);

    private async Task DrainAsync(CancellationToken cancellationToken)
    {
        await TakeOutboxAsync(cancellationToken).ConfigureAwait(false);
        while (_queue.Count > 0)
        {
            var index = Simulation?.PickNext(_queue) ?? 0;
            var envelope = _queue[index];
            _queue.RemoveAt(index);
            if (Simulation?.Duplicate(envelope) == true)
            {
                _queue.Add(envelope); // the broker will deliver it again, later
            }

            if (Responder is not null && envelope is { Kind: MessageKind.Command, Process: not null })
            {
                if (Responder(envelope) is { } reply)
                {
                    _queue.Add(_engine.ReplyTo(envelope, reply));
                }

                continue;
            }

            await _engine.HandleAsync(envelope, cancellationToken).ConfigureAwait(false);
            await TakeOutboxAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>The outbox relay: committed messages are handed to the transport.</summary>
    private async Task TakeOutboxAsync(CancellationToken cancellationToken)
    {
        foreach (var envelope in await _store.TakeOutboxAsync(cancellationToken).ConfigureAwait(false))
        {
            _queue.Add(envelope);
        }
    }

    public void Dispose() => _gate.Dispose();

    /// <summary>Sends what this runtime publishes (integration events) to other services through <paramref name="transport"/>.</summary>
    public InMemoryRuntime UseTransport(IWyrdTransport transport)
    {
        ArgumentNullException.ThrowIfNull(transport);
        _transport = transport;
        return this;
    }

    public async Task ReceiveAsync(TransportMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (!typeof(IIntegrationEvent).IsAssignableFrom(_types.TypeOf(message.Type)))
        {
            throw new InvalidOperationException($"{message.Type} is not an integration event: only integration events cross service boundaries.");
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _queue.Add(new Envelope(message.MessageId, MessageKind.Event, message.Type, message.Payload));
            await DrainAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        await RelayAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The outbox relay to other services: what was published since the last relay goes to the transport, after the
    /// commit and outside the gate (a transport may deliver to another runtime). At least once.
    /// </summary>
    private async Task RelayAsync(CancellationToken cancellationToken)
    {
        if (_transport is null)
        {
            return;
        }

        var pending = Read(() => _store.Published.Skip(_relayed).ToList());
        foreach (var message in pending)
        {
            await _transport.SendAsync(new TransportMessage(message.Id, message.Type, message.Payload), cancellationToken).ConfigureAwait(false);
            _relayed++;
        }
    }

    /// <summary>
    /// Urd: answers the commands processes send instead of their owners (a null answer leaves the command unanswered,
    /// as it was when the history was recorded).
    /// </summary>
    internal Func<Envelope, ReplyMessage?>? Responder { get; set; }

    /// <summary>Urd: only this process reacts to events.</summary>
    internal void ReplayOnly(string process) => _application.Registry.Only = process;

    /// <summary>Urd: delivers a recorded event (a trigger or an awaited event) as it was stored.</summary>
    internal async Task InjectAsync(string type, string payload, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _queue.Add(new Envelope(NextId(), MessageKind.Event, type, payload));
            await DrainAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        await RelayAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The simulator perturbing delivery (Skuld), if any.</summary>
    internal ISimulation? Simulation { get; set; }

    /// <summary>Adds a line to the journal on behalf of the simulator.</summary>
    internal void Note(string message) => Log("skuld", null, message);

    /// <summary>Every process instance and its status, in creation order.</summary>
    internal IReadOnlyList<(string Process, string Key, ProcessStatus Status)> Instances() =>
        Read(() => (IReadOnlyList<(string, string, ProcessStatus)>)[.. _store.Instances.Select(i => (i.Process, i.Key, i.Status))]);

    /// <summary>Short, readable name of a message for journals and reports.</summary>
    internal string Describe(Envelope envelope) => envelope.Kind switch
    {
        MessageKind.Reply => $"reply to {envelope.Process!.Process} {envelope.Process.Key}",
        MessageKind.Timeout => $"timeout of {envelope.Process!.Process} {envelope.Process.Key}",
        _ => $"{_types.TypeOf(envelope.Type).Name}{(envelope.Process is { } p ? $" ({p.Key})" : string.Empty)}",
    };

    private bool Fault(Envelope command, Type commandType)
    {
        if (_faults.GetValueOrDefault(commandType) > 0)
        {
            _faults[commandType]--;
            return true;
        }

        return Simulation?.Fault(command, commandType) ?? false;
    }

    private T Read<T>(Func<T> read)
    {
        _gate.Wait();
        try
        {
            return read();
        }
        finally
        {
            _gate.Release();
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
