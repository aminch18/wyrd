using Wyrd.Runtime;

namespace Wyrd.Testing;

/// <summary>
/// Something that must hold at the end of every future. <c>Check</c> returns null when it holds,
/// or a description of what is wrong.
/// </summary>
public sealed record Invariant(string Name, Func<InMemoryRuntime, string?> Check)
{
    /// <summary>No process is left half-way: every instance completed, was compensated, waits for an outside event, or is suspended.</summary>
    public static Invariant ProcessesSettle { get; } = new("processes settle", runtime =>
    {
        var stuck = runtime.Instances().Where(i => i.Status == ProcessStatus.Running).ToList();
        return stuck.Count == 0 ? null : $"still running: {string.Join(", ", stuck.Select(i => $"{i.Process} {i.Key}"))}";
    });

    /// <summary>No instance was suspended: nothing needs a human.</summary>
    public static Invariant NothingNeedsAttention { get; } = new("nothing needs attention", runtime =>
    {
        var suspended = runtime.Instances().Where(i => i.Status == ProcessStatus.Suspended).ToList();
        return suspended.Count == 0 ? null : $"suspended: {string.Join(", ", suspended.Select(i => $"{i.Process} {i.Key}"))}";
    });

    /// <summary>The invariants every Wyrd application should hold.</summary>
    public static IReadOnlyList<Invariant> Defaults { get; } = [ProcessesSettle, NothingNeedsAttention];
}
