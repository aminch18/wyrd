namespace Wyrd.Runtime;

internal enum MessageKind
{
    Command,
    Event,
    Reply,
    Timeout,
}

/// <summary>Which process instance, step, phase and attempt a message belongs to.</summary>
internal sealed record ProcessRef(string Process, string Key, int Step, bool Compensating, int Attempt = 1);

/// <summary>
/// A message as it travels between services: an id, a kind, a type name and a JSON payload.
/// Nothing but envelopes crosses the transport, in memory or over a broker.
/// </summary>
/// <remarks>
/// <c>Fences</c>: on the undo of a step whose outcome is unknown, the id of the original command. The owner fences it
/// atomically: if it never ran, it never will; if it ran, the undo runs.
/// </remarks>
internal sealed record Envelope(
    Guid MessageId,
    MessageKind Kind,
    string Type,
    string Payload,
    ProcessRef? Process = null,
    Guid? ReplyTo = null,
    Guid? Fences = null);

internal enum ReplyOutcome
{
    Accepted,
    Rejected,
    Faulted,

    /// <summary>Stored for a command that was fenced before it ran: any late copy of it never executes.</summary>
    Fenced,

    /// <summary>The reply to a fencing undo when the original command never ran: there is certainly nothing to undo.</summary>
    NothingToUndo,
}

/// <summary>The framework reply to a command: the accepted event, the rejection error, or a transient fault.</summary>
internal sealed record ReplyMessage(ReplyOutcome Outcome, string? Type = null, string? Payload = null);
