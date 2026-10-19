namespace Wyrd.Runtime;

/// <summary>A deterministic in-memory implementation of the store: same contract as a database, no I/O.</summary>
internal sealed class InMemoryStore : IWyrdStore
{
    private readonly Dictionary<(string StateType, string Key), StoredState> _states = [];
    private readonly Dictionary<(string Process, string Key), StoredInstance> _instances = [];
    private readonly Dictionary<Guid, ReplyMessage> _replies = [];
    private readonly List<StoredMessage> _events = [];
    private readonly List<PublishedMessage> _published = [];
    private readonly List<StoredParked> _parked = [];
    private readonly List<Envelope> _outbox = [];
    private readonly List<(DateTimeOffset At, long Sequence, Envelope Envelope)> _scheduled = [];
    private readonly Dictionary<(string ViewType, string Key), StoredView> _views = [];
    private readonly HashSet<Guid> _handled = [];
    private readonly Dictionary<(string StateType, string Key), long> _projected = [];
    private readonly List<StoredHeld> _held = [];
    private long _sequence;

    /// <summary>
    /// Readers outside the runtime's gate take it; a commit holds it, so they see every commit whole. The runtime's gate
    /// serialises the writer, so the writer's own reads need no lock.
    /// </summary>
    public Lock Sync { get; } = new();

    public IReadOnlyList<StoredMessage> Events => _events;

    public IReadOnlyList<PublishedMessage> Published => _published;

    /// <summary>Messages waiting for their time: timers and scheduled retries.</summary>
    public IEnumerable<Envelope> Scheduled => _scheduled.Select(s => s.Envelope);

    public StoredState? State(string stateType, string key) => _states.GetValueOrDefault((stateType, key));

    public StoredInstance? Instance(string process, string key) => _instances.GetValueOrDefault((process, key));

    public IEnumerable<StoredInstance> Instances => _instances.Values;

    public StoredView? View(string viewType, string key) => _views.GetValueOrDefault((viewType, key));

    public IEnumerable<StoredView> Views(string viewType) => _views.Values.Where(v => v.ViewType == viewType);

    public ValueTask<StoredView?> LoadViewAsync(string viewType, string key, CancellationToken cancellationToken) =>
        ValueTask.FromResult(View(viewType, key));

    public ValueTask<bool> WasHandledAsync(Guid messageId, CancellationToken cancellationToken) =>
        ValueTask.FromResult(_handled.Contains(messageId));

    public ValueTask<StoredState?> LoadStateAsync(string stateType, string key, CancellationToken cancellationToken) =>
        ValueTask.FromResult(State(stateType, key));

    public ValueTask<StoredInstance?> LoadInstanceAsync(string process, string key, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Instance(process, key));

    public ValueTask<ReplyMessage?> FindReplyAsync(Guid commandId, CancellationToken cancellationToken) =>
        ValueTask.FromResult(_replies.GetValueOrDefault(commandId));

    public ValueTask<StoredParked?> FindParkedAsync(string process, string key, int step, CancellationToken cancellationToken) =>
        ValueTask.FromResult(_parked.FirstOrDefault(p => p.Process == process && p.Key == key && p.Step == step));

    public ValueTask<long> LoadProjectedAsync(string stateType, string key, CancellationToken cancellationToken) =>
        ValueTask.FromResult(_projected.GetValueOrDefault((stateType, key)));

    public ValueTask<StoredHeld?> FindHeldAsync(string stateType, string key, long version, CancellationToken cancellationToken) =>
        ValueTask.FromResult(_held.FirstOrDefault(h => h.StateType == stateType && h.Key == key && h.Version == version));

    public ValueTask CommitAsync(Changes changes, CancellationToken cancellationToken)
    {
        lock (Sync)
        {
            // Check every expected version first: all or nothing, like a transaction.
            foreach (var state in changes.States)
            {
                var current = _states.GetValueOrDefault((state.StateType, state.Key))?.Version ?? 0;
                if (current != state.Version - 1)
                {
                    throw new ConcurrencyConflictException($"Concurrency conflict on {state.StateType} '{state.Key}'.");
                }
            }

            foreach (var instance in changes.Instances.Values)
            {
                var current = _instances.GetValueOrDefault((instance.Process, instance.Key))?.Version ?? 0;
                if (current != instance.Version)
                {
                    throw new ConcurrencyConflictException($"Concurrency conflict on process '{instance.Process}' '{instance.Key}'.");
                }
            }

            foreach (var ((stateType, key), (read, _)) in changes.Projected)
            {
                if (_projected.GetValueOrDefault((stateType, key)) != read)
                {
                    throw new InvalidOperationException($"Concurrency conflict projecting {stateType} '{key}'.");
                }
            }

            foreach (var state in changes.States)
            {
                _states[(state.StateType, state.Key)] = state;
            }

            foreach (var ((stateType, key), (_, version)) in changes.Projected)
            {
                _projected[(stateType, key)] = version;
            }

            _held.AddRange(changes.Hold);
            _held.RemoveAll(changes.Release.Contains);

            foreach (var instance in changes.Instances.Values)
            {
                _instances[(instance.Process, instance.Key)] = instance with { Version = instance.Version + 1 };
            }

            foreach (var (id, reply) in changes.Replies)
            {
                _replies[id] = reply;
            }

            foreach (var view in changes.Views.Values)
            {
                if (view.Payload is null)
                {
                    _views.Remove((view.ViewType, view.Key));
                }
                else
                {
                    _views[(view.ViewType, view.Key)] = view;
                }
            }

            _handled.UnionWith(changes.Handled);
            _events.AddRange(changes.Events);
            _published.AddRange(changes.Published);
            _parked.AddRange(changes.Park);
            _parked.RemoveAll(changes.Unpark.Contains);
            _outbox.AddRange(changes.Outbox);
            _scheduled.RemoveAll(s => changes.Cancel.Contains(s.Envelope.MessageId));
            foreach (var scheduled in changes.Schedule)
            {
                _scheduled.Add((scheduled.At, _sequence++, scheduled.Envelope));
            }
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyList<Envelope>> TakeOutboxAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<Envelope> taken = [.. _outbox];
        _outbox.Clear();
        return ValueTask.FromResult(taken);
    }

    public ValueTask<Scheduled?> TakeNextDueAsync(DateTimeOffset until, CancellationToken cancellationToken)
    {
        if (_scheduled.Count == 0)
        {
            return ValueTask.FromResult<Scheduled?>(null);
        }

        var next = _scheduled.OrderBy(s => s.At).ThenBy(s => s.Sequence).First();
        if (next.At > until)
        {
            return ValueTask.FromResult<Scheduled?>(null);
        }

        _scheduled.Remove(next);
        return ValueTask.FromResult<Scheduled?>(new Scheduled(next.At, next.Envelope));
    }
}
