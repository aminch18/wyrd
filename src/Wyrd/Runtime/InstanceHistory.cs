using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wyrd.Runtime;

/// <summary>What crossed a process instance's boundary.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<HistoryKind>))]
public enum HistoryKind
{
    /// <summary>The trigger event started the instance.</summary>
    Started,

    /// <summary>The process sent a command: a step, an undo or the outcome.</summary>
    Sent,

    /// <summary>The owner answered a command.</summary>
    Replied,

    /// <summary>An awaited event arrived.</summary>
    Received,

    /// <summary>A <c>.Within</c> expired.</summary>
    TimedOut,

    /// <summary>An operator resumed the suspended instance.</summary>
    Resumed,

    /// <summary>An operator asked the suspended instance to compensate.</summary>
    CompensationRequested,

    /// <summary>The instance needs operator attention before it can continue safely.</summary>
    Suspended,

    /// <summary>All forward steps finished.</summary>
    Completed,

    /// <summary>All required undos and the compensation outcome finished.</summary>
    Compensated,
}

/// <summary>
/// One entry of an instance's history. <c>Type</c> and <c>Payload</c> are the message as stored (the trigger, a command,
/// the event or error of a reply, an awaited event); <c>Step</c> is the node index (the node count for the outcome).
/// </summary>
public sealed record HistoryEntry(DateTimeOffset At, HistoryKind Kind)
{
    /// <summary>The registered message type name, when this entry records a message.</summary>
    public string? Type { get; init; }

    /// <summary>The serialized message, reply event or error, when present.</summary>
    public string? Payload { get; init; }

    /// <summary>The zero-based node index; the node count identifies the compensation outcome.</summary>
    public int? Step { get; init; }

    /// <summary>Whether the entry belongs to the reverse compensation flow.</summary>
    public bool Compensating { get; init; }

    /// <summary>The command attempt number when this entry records an attempt.</summary>
    public int? Attempt { get; init; }

    /// <summary>For a reply: Accepted, Rejected, Faulted, Fenced or NothingToUndo.</summary>
    public string? Outcome { get; init; }

    /// <summary>For a suspension: why.</summary>
    public string? Reason { get; init; }
}

/// <summary>
/// Everything a process instance received and sent, in order: enough to replay it exactly (Urd). Export it with
/// <see cref="ToJson"/>, replay it with <c>Urd.ReplayAsync</c> from Wyrd.Testing.
/// </summary>
public sealed record InstanceHistory(string Process, string Key, string Fingerprint, IReadOnlyList<HistoryEntry> Entries)
{
    /// <summary>Exports the history as JSON using Wyrd's generated metadata, suitable for the replay tool.</summary>
    /// <example><code>File.WriteAllText("order.history.json", history.ToJson());</code></example>
    public string ToJson() => JsonSerializer.Serialize(this, HistoryJsonContext.Default.InstanceHistory);

    /// <summary>Reads an exported history for inspection or replay; malformed or empty history input is rejected.</summary>
    public static InstanceHistory FromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return JsonSerializer.Deserialize(json, HistoryJsonContext.Default.InstanceHistory)
            ?? throw new InvalidOperationException("The history is empty.");
    }
}
