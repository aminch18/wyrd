using System.Collections.Immutable;

namespace Wyrd.Runtime;

internal sealed record StoredState(string StateType, string Key, string Type, string Payload, long Version);

internal sealed record StoredMessage(string Type, string Payload);

/// <summary>
/// An integration event this service published, with the id it keeps when it travels to other services and the trace
/// context it was published in (diagnostic only).
/// </summary>
internal sealed record PublishedMessage(Guid Id, string Type, string Payload, TraceContext? Trace = null);

internal sealed record StoredParked(string Process, string Key, int Step, string Type, string Payload)
{
    public StoredMessage AsFact() => new(Type, Payload);
}

/// <summary>
/// Where a process instance is. In a database this is one row. <c>Uncertain</c> maps the steps the process gave up on
/// without knowing whether they ran to their original command id: their undo fences that command. <c>Fingerprint</c> is
/// the structure of the process the instance started with (see <see cref="Processes.Process.Fingerprint"/>). <c>Facts</c>
/// are the events its steps produced and the events it awaited, in order: later steps can read them. <c>History</c> is
/// what crossed its boundary (Urd), committed with the instance itself. <c>KeptReply</c> is the reply to the pending
/// command that arrived while the deployed definition was not the one the instance started with: the operator's resume
/// or compensation takes it, because the owner's inbox forgets it after its deduplication retention.
/// </summary>
internal sealed record StoredInstance(
    string Process,
    string Key,
    string TriggerType,
    string TriggerPayload,
    int Position,
    ProcessStatus Status,
    ImmutableList<int> Completed,
    ImmutableDictionary<int, Guid> Uncertain,
    ImmutableList<StoredMessage> Facts,
    int Attempt,
    Guid? Pending,
    bool Compensating,
    DateTimeOffset? Deadline,
    string Fingerprint,
    ImmutableList<HistoryEntry> History,
    long Version,
    string? Reason = null,
    TraceContext? Trace = null,
    Guid? Timer = null,
    Envelope? KeptReply = null);

internal sealed record Scheduled(DateTimeOffset At, Envelope Envelope);

/// <summary>A domain event that arrived before an earlier event of its stream: read models get it once the gap is filled.</summary>
internal sealed record StoredHeld(string StateType, string Key, long Version, string Type, string Payload);

/// <summary>A stream a catching-up projection follows: its view was seeded from the state, or the stream is newer.</summary>
/// <remarks>The stream's key, not the view's: one view can be seeded from many states.</remarks>
internal sealed record StoredSeeded(string ViewType, string StateType, string Key);

/// <summary>A read-model document. A null payload deletes it.</summary>
internal sealed record StoredView(string ViewType, string Key, string? Type, string? Payload);

/// <summary>
/// Everything that handling one message changes. A store commits it atomically: state, events, process
/// instances, the inbox entry and the outgoing messages (outbox) succeed or fail together.
/// </summary>
internal sealed class Changes
{
    public List<StoredState> States { get; } = [];

    public Dictionary<(string Process, string Key), StoredInstance> Instances { get; } = [];

    public List<StoredMessage> Events { get; } = [];

    public List<PublishedMessage> Published { get; } = [];

    public Dictionary<Guid, ReplyMessage> Replies { get; } = [];

    public List<Envelope> Outbox { get; } = [];

    public List<Scheduled> Schedule { get; } = [];

    /// <summary>Scheduled messages (by id) that are no longer wanted: a wait's timer once the wait is over.</summary>
    public List<Guid> Cancel { get; } = [];

    public List<StoredParked> Park { get; } = [];

    public List<StoredParked> Unpark { get; } = [];

    public Dictionary<(string ViewType, string Key), StoredView> Views { get; } = [];

    /// <summary>
    /// The last version of each stream projected into read models, with the version read (checked on commit).
    /// </summary>
    public Dictionary<(string StateType, string Key), (long Read, long Version)> Projected { get; } = [];

    public List<StoredHeld> Hold { get; } = [];

    public List<StoredHeld> Release { get; } = [];

    /// <summary>
    /// Streams a catching-up projection now follows (see <see cref="Projection{TView}.Seed{TState}"/>), with the version
    /// its seed was built from: 0 for a stream that began after the projection was added. Later events reach the
    /// projection; earlier ones are in the seed.
    /// </summary>
    public Dictionary<StoredSeeded, long> Seeded { get; } = [];

    /// <summary>States a seed was built from: checked on commit, so an event that changed the state meanwhile wins.</summary>
    public List<StoredState> Unchanged { get; } = [];

    /// <summary>Views whose projection has caught up: their seed marks go.</summary>
    public List<string> CaughtUp { get; } = [];

    /// <summary>Events handled by this transaction: a redelivery of the same message id is skipped.</summary>
    public List<Guid> Handled { get; } = [];

    /// <summary>Saves an instance; its <c>Version</c> is the one read, checked on commit (optimistic concurrency).</summary>
    public void Save(StoredInstance instance)
    {
        Tracing.Instance(instance);
        Instances[(instance.Process, instance.Key)] = instance;
    }

    public StoredInstance? Find(string process, string key) =>
        Instances.TryGetValue((process, key), out var instance) ? instance : null;
}

/// <summary>
/// What the engine needs from persistence: read, and commit one message's changes atomically. In-memory and
/// PostgreSQL implement it; how committed messages and timers are delivered is up to each host.
/// </summary>
internal interface IWyrdStore
{
    ValueTask<StoredState?> LoadStateAsync(string stateType, string key, CancellationToken cancellationToken);

    ValueTask<StoredInstance?> LoadInstanceAsync(string process, string key, CancellationToken cancellationToken);

    ValueTask<ReplyMessage?> FindReplyAsync(Guid commandId, CancellationToken cancellationToken);

    ValueTask<StoredParked?> FindParkedAsync(string process, string key, int step, CancellationToken cancellationToken);

    ValueTask<StoredView?> LoadViewAsync(string viewType, string key, CancellationToken cancellationToken);

    /// <summary>The last version of a stream projected into read models (0 when none).</summary>
    ValueTask<long> LoadProjectedAsync(string stateType, string key, CancellationToken cancellationToken);

    ValueTask<StoredHeld?> FindHeldAsync(string stateType, string key, long version, CancellationToken cancellationToken);

    ValueTask<bool> WasHandledAsync(Guid messageId, CancellationToken cancellationToken);

    /// <summary>Whether the projection of a view has caught up from existing states.</summary>
    ValueTask<bool> IsCaughtUpAsync(string viewType, CancellationToken cancellationToken);

    /// <summary>The version a catching-up projection's seed of a stream was built from (null while not seeded).</summary>
    ValueTask<long?> LoadSeededAsync(StoredSeeded seeded, CancellationToken cancellationToken);

    /// <summary>
    /// Whether an event already in one of a catching-up projection's seeds is still to be projected: until it is, the
    /// seed marks are what keeps it from being applied twice.
    /// </summary>
    ValueTask<bool> SeedsAheadAsync(string viewType, CancellationToken cancellationToken);

    /// <summary>States of a type that a catching-up projection doesn't follow yet, at most <paramref name="max"/>.</summary>
    ValueTask<IReadOnlyList<StoredState>> UnseededAsync(string viewType, string stateType, int max, CancellationToken cancellationToken);

    ValueTask CommitAsync(Changes changes, CancellationToken cancellationToken);

}
