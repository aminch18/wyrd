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
    internal Process(string name, Type trigger, Func<object, object> keyOf, ImmutableArray<ProcessNode> nodes)
    {
        Name = name;
        Trigger = trigger;
        KeyOf = keyOf;
        Nodes = nodes;
    }

    public string Name { get; }

    public Type Trigger { get; }

    internal Func<object, object> KeyOf { get; }

    public ImmutableArray<ProcessNode> Nodes { get; }

    /// <summary>Starts a process on an event. <paramref name="key"/> correlates the instance, e.g. <c>e => e.OrderId</c>.</summary>
    public static ProcessBuilder<TEvent> StartOn<TEvent>(Func<TEvent, object> key, [CallerMemberName] string name = "")
        where TEvent : IEvent
    {
        ArgumentNullException.ThrowIfNull(key);
        return new(name, e => key((TEvent)e), []);
    }

    /// <summary>Two processes are equal when they have the same trigger and the same nodes, in order.</summary>
    public bool Equals(Process? other) =>
        other is not null && Name == other.Name && Trigger == other.Trigger && Nodes.SequenceEqual(other.Nodes);

    public override bool Equals(object? obj) => Equals(obj as Process);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Name);
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
                    var within = wait.Timeout is { } t ? $"<br/>within {Durations.Format(t)}" : "";
                    sb.AppendLine(CultureInfo.InvariantCulture, $"    {id}{{{{\"await {wait.Event.Name}{within}\"}}}}");
                    break;
            }

            sb.AppendLine(CultureInfo.InvariantCulture, $"    {previous} --> {id}");
            previous = id;
        }

        sb.AppendLine(CultureInfo.InvariantCulture, $"    {previous} --> done([\"completed\"])");
        return sb.ToString();
    }
}
