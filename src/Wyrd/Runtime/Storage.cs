using System.Collections.Immutable;

namespace Wyrd.Runtime;

internal sealed record StoredState(string StateType, string Key, string Type, string Payload, long Version);

internal sealed record StoredMessage(string Type, string Payload);

internal sealed record StoredParked(string Process, string Key, int Step, string Type, string Payload);

/// <summary>
/// Where a process instance is. In a database this is one row. <c>Uncertain</c> maps the steps the process gave up on
/// without knowing whether they ran to their original command id: their undo fences that command. <c>Fingerprint</c> is
/// the structure of the process the instance started with (see <see cref="Processes.Process.Fingerprint"/>).
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
    int Attempt,
    Guid? Pending,
    bool Compensating,
    DateTimeOffset? Deadline,
    string Fingerprint,
    long Version,
    string? Reason = null);

internal sealed record Scheduled(DateTimeOffset At, Envelope Envelope);

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

    public List<StoredMessage> Published { get; } = [];

    public Dictionary<Guid, ReplyMessage> Replies { get; } = [];

    public List<Envelope> Outbox { get; } = [];

    public List<Scheduled> Schedule { get; } = [];

    public List<StoredParked> Park { get; } = [];

    public List<StoredParked> Unpark { get; } = [];

    public Dictionary<(string ViewType, string Key), StoredView> Views { get; } = [];

    /// <summary>Events handled by this transaction: a redelivery of the same message id is skipped.</summary>
    public List<Guid> Handled { get; } = [];

    /// <summary>Saves an instance; its <c>Version</c> is the one read, checked on commit (optimistic concurrency).</summary>
    public void Save(StoredInstance instance) => Instances[(instance.Process, instance.Key)] = instance;

    public StoredInstance? Find(string process, string key) =>
        Instances.TryGetValue((process, key), out var instance) ? instance : null;
}

/// <summary>The persistence port. In memory today; PostgreSQL and others implement the same contract.</summary>
internal interface IWyrdStore
{
    ValueTask<StoredState?> LoadStateAsync(string stateType, string key, CancellationToken cancellationToken);

    ValueTask<StoredInstance?> LoadInstanceAsync(string process, string key, CancellationToken cancellationToken);

    ValueTask<ReplyMessage?> FindReplyAsync(Guid commandId, CancellationToken cancellationToken);

    ValueTask<StoredParked?> FindParkedAsync(string process, string key, int step, CancellationToken cancellationToken);

    ValueTask<StoredView?> LoadViewAsync(string viewType, string key, CancellationToken cancellationToken);

    ValueTask<bool> WasHandledAsync(Guid messageId, CancellationToken cancellationToken);

    ValueTask CommitAsync(Changes changes, CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<Envelope>> TakeOutboxAsync(CancellationToken cancellationToken);

    ValueTask<Scheduled?> TakeNextDueAsync(DateTimeOffset until, CancellationToken cancellationToken);
}
