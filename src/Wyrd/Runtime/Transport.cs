namespace Wyrd.Runtime;

/// <summary>
/// An integration event on its way to or from another service: its contract (as registered, e.g.
/// <c>payments.payment-charged v1</c>), its payload, and the id it keeps end to end so a redelivery is recognised.
/// </summary>
public sealed record TransportMessage(Guid MessageId, string Type, string Payload)
{
    /// <summary>
    /// The W3C <c>traceparent</c> it was published in, if traced: a broker carries it as a header, and the receiving
    /// service continues the trace. Diagnostic only, never part of the message's identity.
    /// </summary>
    public string? TraceParent { get; init; }

    /// <summary>The W3C <c>tracestate</c> that goes with <see cref="TraceParent"/>.</summary>
    public string? TraceState { get; init; }
}

/// <summary>
/// Carries integration events between services (RabbitMQ, or <see cref="InMemoryTransport"/> in tests). Only
/// integration events cross service boundaries: commands have one owner inside one service.
/// </summary>
public interface IWyrdTransport
{
    /// <summary>Sends a published integration event. At least once: the receiving runtime discards duplicates.</summary>
    Task SendAsync(TransportMessage message, CancellationToken cancellationToken = default);
}

/// <summary>
/// Connects runtimes in one process, deterministically: every message reaches every subscriber, in order, before
/// <see cref="SendAsync"/> returns. A subscriber ignores contracts it doesn't use and discards its own messages as
/// duplicates, like it would behind a broker.
/// </summary>
public sealed class InMemoryTransport : IWyrdTransport
{
    private readonly List<IWyrdRuntime> _subscribers = [];
    private readonly List<InMemoryRuntime> _members = [];
    private readonly List<TransportMessage> _sent = [];
    private readonly Lock _lock = new();

    /// <summary>Delivers what is sent through this transport to <paramref name="runtime"/>.</summary>
    public InMemoryTransport Subscribe(IWyrdRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        if (runtime is InMemoryRuntime member)
        {
            Join(member);
        }

        lock (_lock)
        {
            _subscribers.Add(runtime);
        }

        return this;
    }

    /// <summary>Numbers the in-memory runtimes it connects, in order, so their message ids never collide.</summary>
    internal void Join(InMemoryRuntime runtime)
    {
        long member;
        lock (_lock)
        {
            var index = _members.IndexOf(runtime);
            if (index < 0)
            {
                _members.Add(runtime);
                index = _members.Count - 1;
            }

            member = index + 1;
        }

        runtime.JoinAs(member);
    }

    /// <summary>Every message sent, in order.</summary>
    public IReadOnlyList<TransportMessage> Sent
    {
        get
        {
            lock (_lock)
            {
                return [.. _sent];
            }
        }
    }

    public async Task SendAsync(TransportMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        IWyrdRuntime[] subscribers;
        lock (_lock)
        {
            _sent.Add(message);
            subscribers = [.. _subscribers];
        }

        foreach (var subscriber in subscribers)
        {
            await subscriber.ReceiveAsync(message, cancellationToken).ConfigureAwait(false);
        }
    }
}
