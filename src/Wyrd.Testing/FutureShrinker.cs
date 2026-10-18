namespace Wyrd.Testing;

/// <summary>Greedily removes individual perturbations, then pairs, within a fixed replay budget.</summary>
internal static class FutureShrinker
{
    internal static async Task<ShrunkFuture> ShrinkAsync(
        IReadOnlyList<SimulationDecision> decisions,
        ShrunkFuture original,
        Func<IReadOnlyList<SimulationDecision>, Task<ShrunkFuture?>> replay,
        int budget)
    {
        var current = decisions.ToArray();
        var best = original;
        var replays = 0;

        // A baseline failure needs no hostile delivery at all.
        if (budget > 0)
        {
            replays++;
            if (await replay(current.Select(d => d with { Answer = 0 }).ToArray()).ConfigureAwait(false) is { } neutral)
            {
                return neutral with { Replays = replays };
            }
        }

        while (replays < budget)
        {
            var reduced = false;
            foreach (var candidate in Removals(current))
            {
                if (replays >= budget)
                {
                    break;
                }

                replays++;
                if (await replay(candidate).ConfigureAwait(false) is not { } result)
                {
                    continue;
                }

                current = candidate;
                best = result;
                reduced = true;
                break; // Revisit earlier decisions: the successful removal may make them unnecessary now.
            }

            if (!reduced)
            {
                break;
            }
        }

        return best with { Replays = replays };
    }

    private static IEnumerable<SimulationDecision[]> Removals(SimulationDecision[] current)
    {
        var positions = current.Select((decision, index) => (decision, index))
            .Where(item => item.decision.Answer != 0).Select(item => item.index).ToArray();

        foreach (var position in positions)
        {
            var candidate = (SimulationDecision[])current.Clone();
            candidate[position] = candidate[position] with { Answer = 0 };
            yield return candidate;
        }

        for (var first = 0; first < positions.Length; first++)
        {
            for (var second = first + 1; second < positions.Length; second++)
            {
                var candidate = (SimulationDecision[])current.Clone();
                candidate[positions[first]] = candidate[positions[first]] with { Answer = 0 };
                candidate[positions[second]] = candidate[positions[second]] with { Answer = 0 };
                yield return candidate;
            }
        }
    }
}
