namespace Wyrd.Runtime;

internal enum MessageKind
{
    Command,
    Event,
    Reply,
    Timeout,
}

/// <summary>Which process instance, step and phase a message belongs to.</summary>
internal sealed record ProcessRef(string Process, string Key, int Step, bool Compensating);

/// <summary>
/// A message as it travels between services: an id, a kind, a type name and a JSON payload.
/// Nothing but envelopes crosses the transport, in memory or over a broker.
/// </summary>
internal sealed record Envelope(
    Guid MessageId,
    MessageKind Kind,
    string Type,
    string Payload,
    ProcessRef? Process = null,
    Guid? ReplyTo = null);

internal enum ReplyOutcome
{
    Accepted,
    Rejected,
    Faulted,
}

/// <summary>The framework reply to a command: the accepted event, the rejection error, or a transient fault.</summary>
internal sealed record ReplyMessage(ReplyOutcome Outcome, string? Type = null, string? Payload = null);
