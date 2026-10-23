using System.Runtime.ExceptionServices;

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
    Guid? Fences = null,
    StreamRef? Stream = null,
    TraceContext? Trace = null);

/// <summary>Where a domain event comes from: the state it changed and that state's version after the change.</summary>
internal sealed record StreamRef(string StateType, string Key, long Version);

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

/// <summary>
/// What an integration's outside call returned, made before the transaction that records it: the decision, or the
/// failure to record as a fault (or to throw, for a command no process sent).
/// </summary>
internal sealed record IntegrationCall(Decision? Decision = null, ExceptionDispatchInfo? Failure = null)
{
    /// <summary>No call was made: handling the command won't need one (a duplicate, a fault, a fence).</summary>
    public static IntegrationCall None { get; } = new();
}

/// <summary>
/// Handling a command needs the outside call after all (what it read changed since <see cref="Engine.CallAsync"/>
/// decided no call was needed): the host rolls back and handles the message again.
/// </summary>
internal sealed class IntegrationCallNeededException()
    : WyrdException("The command's outside call wasn't made before its transaction: handle the message again.");
