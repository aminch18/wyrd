namespace Wyrd.Testing;

/// <summary>How many futures Skuld explores and how hostile they are.</summary>
public sealed record SimulationOptions
{
    /// <summary>How many futures to explore. Each one is a different seed.</summary>
    public int Runs { get; init; } = 200;

    /// <summary>The first seed. Run <c>i</c> uses <c>Seed + i</c>, so any failing run can be replayed alone.</summary>
    public int Seed { get; init; } = 1;

    /// <summary>Probability that a command sent by a process fails transiently.</summary>
    public double FaultRate { get; init; } = 0.1;

    /// <summary>Probability that a message is delivered a second time (at-least-once delivery).</summary>
    public double DuplicateRate { get; init; } = 0.1;

    /// <summary>Probability that the next message is picked out of order.</summary>
    public double ReorderRate { get; init; } = 0.3;

    /// <summary>How long the clock runs after the scenario, so every retry and timeout fires.</summary>
    public TimeSpan SettleFor { get; init; } = TimeSpan.FromDays(1);

    /// <summary>Replays exactly one future.</summary>
    public SimulationOptions Only(int seed) => this with { Seed = seed, Runs = 1 };
}
