namespace Wyrd.Runtime;

/// <summary>
/// An integration event on its way to or from another service: its contract (as registered, e.g.
/// <c>payments.payment-charged v1</c>), its payload, and the id it keeps end to end so a redelivery is recognised.
/// </summary>
public sealed record TransportMessage(Guid MessageId, string Type, string Payload);

/// <summary>
/// Carries integration events between services (RabbitMQ, or <see cref="InMemoryTransport"/> in tests). Only
/// integration events cross service boundaries: commands have one owner inside one service.
/// </summary>
public interface IWyrdTransport
{
    /// <summary>Sends a published integration event. At least once: the receiving runtime discards duplicates.</summary>
    Task SendAsync(TransportMessage message, CancellationToken cancellationToken = default);
}

/// <summary>Connects runtimes in one process, deterministically: every message reaches every other subscriber.</summary>
public sealed class InMemoryTransport : IWyrdTransport
{
    private readonly List<IWyrdRuntime> _subscribers = [];

    /// <summary>Delivers what other services publish to <paramref name="runtime"/>.</summary>
    public InMemoryTransport Subscribe(IWyrdRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        _subscribers.Add(runtime);
        return this;
    }

    /// <summary>Every message sent, in order.</summary>
    public List<TransportMessage> Sent { get; } = [];

    public async Task SendAsync(TransportMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        Sent.Add(message);
        foreach (var subscriber in _subscribers)
        {
            await subscriber.ReceiveAsync(message, cancellationToken).ConfigureAwait(false);
        }
    }
}
