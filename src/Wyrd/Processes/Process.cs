using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace Wyrd.Processes;

/// <summary>
/// A business process described as data: the trigger and its ordered steps.
/// It is the single source of truth for the runtime, the simulator and the generated docs.
/// </summary>
public sealed class Process : IEquatable<Process>
{
    internal Process(string name, Type trigger, Func<object, object> keyOf, ImmutableArray<ProcessNode> nodes, CompensatedNode? outcome)
    {
        Name = name;
        Trigger = trigger;
        KeyOf = keyOf;
        Nodes = nodes;
        Outcome = outcome;
    }

    /// <summary>The process name used to identify instances, histories, journals and diagrams.</summary>
    public string Name { get; }

    /// <summary>The event type that starts an instance when its correlated key is first seen.</summary>
    public Type Trigger { get; }

    internal Func<object, object> KeyOf { get; }

    /// <summary>The steps and waits in execution order. The compensation outcome is stored separately.</summary>
    public ImmutableArray<ProcessNode> Nodes { get; }

    /// <summary>The command sent once compensation has finished, and its retry policy, if any.</summary>
    public CompensatedNode? Outcome { get; }

    /// <summary>The command sent once compensation has finished, if any.</summary>
    public StepRef? OnCompensated => Outcome?.Action;

    /// <summary>Starts a process on an event. <paramref name="key"/> correlates the instance, e.g. <c>e => e.OrderId</c>.</summary>
    public static ProcessBuilder<TEvent> StartOn<TEvent>(Func<TEvent, object> key, [CallerMemberName] string name = "")
        where TEvent : IEvent
    {
        ArgumentNullException.ThrowIfNull(key);
        return new(name, e => key((TEvent)e), []);
    }

    /// <summary>
    /// The process's structure — its steps, undos, waits and outcome, in order — as a short hash. Configuration such
    /// as retries or time limits is not part of it. Each instance remembers the fingerprint it started with: if the
    /// deployed definition's structure differs, the instance is suspended instead of continuing at the wrong step.
    /// </summary>
    public string Fingerprint => _fingerprint ??= ComputeFingerprint();

    private string? _fingerprint;

    private string ComputeFingerprint()
    {
        var shape = new StringBuilder(Trigger.Name);
        foreach (var node in Nodes)
        {
            shape.Append(node switch
            {
                StepNode step => $" > {step.Action}" + (step.Compensation is { } undo ? $" ~ {undo}" : ""),
                AwaitNode wait => $" > await {wait.Event.Name}",
                _ => throw new InvalidOperationException($"Unknown node {node.GetType().Name}."),
            });
        }

        if (Outcome is { } outcome)
        {
            shape.Append(CultureInfo.InvariantCulture, $" ! {outcome.Action}");
        }

        var hash = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(shape.ToString()));
        return Convert.ToHexStringLower(hash)[..12];
    }

    /// <summary>Two processes are equal when they have the same trigger and the same nodes, in order.</summary>
    public bool Equals(Process? other) =>
        other is not null && Name == other.Name && Trigger == other.Trigger && Nodes.SequenceEqual(other.Nodes)
        && Equals(Outcome, other.Outcome);

    /// <summary>Compares the name, trigger, ordered nodes and compensation outcome with another process.</summary>
    public override bool Equals(object? obj) => Equals(obj as Process);

    /// <summary>Returns a hash of the same process data used by equality.</summary>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Name);
        hash.Add(Trigger);
        hash.Add(Outcome);
        foreach (var node in Nodes)
        {
            hash.Add(node);
        }

        return hash.ToHashCode();
    }

    /// <summary>Renders the process as a Mermaid flowchart (living documentation).</summary>
    public string ToMermaid()
    {
        var sb = new StringBuilder().AppendLine("flowchart TD");
        sb.AppendLine(CultureInfo.InvariantCulture, $"    start([\"{Trigger.Name}\"])");

        var previous = "start";
        for (var i = 0; i < Nodes.Length; i++)
        {
            var id = $"n{i + 1}";
            switch (Nodes[i])
            {
                case StepNode step:
                    var retry = step.Retry is { } r ? $"<br/>retry {r.Attempts} {r.Backoff.ToString().ToLowerInvariant()}" : "";
                    sb.AppendLine(CultureInfo.InvariantCulture, $"    {id}[\"{step.Action}{retry}\"]");
                    if (step.Compensation is { } undo)
                    {
                        sb.AppendLine(CultureInfo.InvariantCulture, $"    {id} -. undo .-> {id}u[\"{undo}\"]");
                    }

                    break;
                case AwaitNode wait:
                    var within = wait.Timeout is { } t ? $"<br/>within {Durations.Format(t)}" : "";
                    sb.AppendLine(CultureInfo.InvariantCulture, $"    {id}{{{{\"await {wait.Event.Name}{within}\"}}}}");
                    break;
            }

            sb.AppendLine(CultureInfo.InvariantCulture, $"    {previous} --> {id}");
            previous = id;
        }

        sb.AppendLine(CultureInfo.InvariantCulture, $"    {previous} --> done([\"completed\"])");
        if (Outcome is { } outcome)
        {
            var retry = outcome.Retry is { } r ? $"<br/>retry {r.Attempts} {r.Backoff.ToString().ToLowerInvariant()}" : "";
            sb.AppendLine(CultureInfo.InvariantCulture, $"    compensated([\"compensated\"]) --> outcome[\"{outcome.Action}{retry}\"]");
        }
        return sb.ToString();
    }
}
