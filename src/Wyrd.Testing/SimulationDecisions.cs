using Wyrd.Runtime;

namespace Wyrd.Testing;

internal enum SimulationHook
{
    PickNext,
    Duplicate,
    Fault,
}

/// <summary>One hook call, including neutral answers. Positions stay fixed when a perturbation is disabled.</summary>
internal sealed record SimulationDecision(SimulationHook Hook, int Answer);

/// <summary>Records random decisions, or replays them without consulting a random source.</summary>
internal sealed class SimulationDecisions(
    InMemoryRuntime runtime,
    Random random,
    SimulationOptions options,
    IReadOnlyList<SimulationDecision>? replay = null) : ISimulation
{
    private readonly HashSet<Envelope> _duplicated = [];
    private readonly List<SimulationDecision> _recorded = [];
    private int _position;

    public IReadOnlyList<SimulationDecision> Recorded => _recorded;

    private int Decide(SimulationHook hook, Func<int> draw)
    {
        var answer = 0;
        if (replay is null)
        {
            answer = draw();
        }
        else if (_position < replay.Count && replay[_position].Hook == hook)
        {
            answer = replay[_position].Answer;
        }

        _position++;
        _recorded.Add(new SimulationDecision(hook, answer));
        return answer;
    }

    public int PickNext(IReadOnlyList<Envelope> queue)
    {
        var index = Decide(SimulationHook.PickNext, () =>
            queue.Count < 2 || random.NextDouble() >= options.ReorderRate ? 0 : random.Next(1, queue.Count));
        // Removing earlier decisions can leave fewer messages at this position in a replay.
        if (index <= 0 || index >= queue.Count)
        {
            return 0;
        }

        runtime.Note($"delivered {runtime.Describe(queue[index])} before {runtime.Describe(queue[0])}");
        return index;
    }

    public bool Duplicate(Envelope envelope)
    {
        var duplicate = Decide(SimulationHook.Duplicate, () =>
            _duplicated.Contains(envelope) || random.NextDouble() >= options.DuplicateRate ? 0 : 1);
        if (duplicate == 0 || !_duplicated.Add(envelope))
        {
            return false;
        }

        runtime.Note($"will deliver {runtime.Describe(envelope)} twice");
        return true;
    }

    public bool Fault(Envelope command, Type commandType)
    {
        if (Decide(SimulationHook.Fault, () => random.NextDouble() >= options.FaultRate ? 0 : 1) == 0)
        {
            return false;
        }

        runtime.Note($"{runtime.Describe(command)} failed transiently");
        return true;
    }
}
