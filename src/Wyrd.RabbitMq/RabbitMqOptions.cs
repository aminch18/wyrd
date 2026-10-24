namespace Wyrd.RabbitMq;

/// <summary>How Wyrd uses RabbitMQ. The defaults suit most services; every service of a system shares the exchange.</summary>
public sealed record RabbitMqOptions
{
    /// <summary>The durable topic exchange every service publishes to; routing keys are contract names.</summary>
    public string Exchange { get; init; } = "wyrd";

    /// <summary>How many messages a subscriber holds unacknowledged at once. At least 1: RabbitMQ reads 0 as unlimited.</summary>
    public ushort Prefetch { get; init; } = 16;

    /// <summary>
    /// How long a publish waits for the broker's confirm. A publish not confirmed in time fails, and the message stays in
    /// the outbox to be sent again; the receiving runtime discards a duplicate.
    /// </summary>
    public TimeSpan ConfirmTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>How long a subscriber waits before handing back a message it couldn't deliver to its runtime.</summary>
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(1);
}
