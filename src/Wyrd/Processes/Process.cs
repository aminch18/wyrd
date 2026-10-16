using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace Wyrd.Processes;

/// <summary>
/// A business process described as data: the trigger and its ordered steps.
/// It is the single source of truth for the runtime, the simulator and the generated docs.
/// </summary>
public sealed class Process : IEquatable<Process>
{
    internal Process(Type trigger, ImmutableArray<ProcessNode> nodes)
    {
        Trigger = trigger;
        Nodes = nodes;
    }

    public Type Trigger { get; }

    public ImmutableArray<ProcessNode> Nodes { get; }

    public static ProcessBuilder StartOn<TEvent>() => new(typeof(TEvent), []);

    /// <summary>Two processes are equal when they have the same trigger and the same nodes, in order.</summary>
    public bool Equals(Process? other) =>
        other is not null && Trigger == other.Trigger && Nodes.SequenceEqual(other.Nodes);

    public override bool Equals(object? obj) => Equals(obj as Process);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Trigger);
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
                    var within = wait.Timeout is { } t ? $"<br/>within {Format(t)}" : "";
                    sb.AppendLine(CultureInfo.InvariantCulture, $"    {id}{{{{\"await {wait.Event.Name}{within}\"}}}}");
                    break;
            }

            sb.AppendLine(CultureInfo.InvariantCulture, $"    {previous} --> {id}");
            previous = id;
        }

        sb.AppendLine(CultureInfo.InvariantCulture, $"    {previous} --> done([\"completed\"])");
        return sb.ToString();
    }

    private static string Format(TimeSpan t) => t switch
    {
        { TotalDays: >= 1 } when t.TotalDays % 1 == 0 => $"{t.TotalDays}d",
        { TotalHours: >= 1 } when t.TotalHours % 1 == 0 => $"{t.TotalHours}h",
        { TotalMinutes: >= 1 } when t.TotalMinutes % 1 == 0 => $"{t.TotalMinutes}m",
        _ => $"{t.TotalSeconds}s",
    };
}
