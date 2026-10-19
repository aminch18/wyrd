using System.Collections;
using System.Security.Cryptography;
using System.Text;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Wyrd.Tool;

internal static class Processes
{
    public const string Header = "# wyrd processes 1";

    public static IReadOnlyList<ProcessSnapshot> Discover(string assemblyPath) =>
        ProcessDiscovery.Read(assemblyPath, (_, members) =>
        {
            var snapshots = members.Select(Capture).ToArray();
            Validate(snapshots);
            return snapshots;
        });

    // Read the public model, not ToMermaid: diagrams also include configuration that the fingerprint excludes.
    internal static ProcessSnapshot Capture(ProcessMember member)
    {
        var process = member.Value;
        var structure = new List<string> { "trigger " + ((Type)Required(process, "Trigger")).Name };
        var shape = new StringBuilder(((Type)Required(process, "Trigger")).Name);
        foreach (var node in (IEnumerable)Required(process, "Nodes"))
        {
            switch (node.GetType().FullName)
            {
                case "Wyrd.Processes.StepNode":
                    var action = Required(node, "Action").ToString()!;
                    var undo = node.GetType().GetProperty("Compensation")?.GetValue(node);
                    var step = action + (undo is null ? "" : $" ~ {undo}");
                    structure.Add("step " + step);
                    shape.Append(" > ").Append(step);
                    break;
                case "Wyrd.Processes.AwaitNode":
                    var eventName = ((Type)Required(node, "Event")).Name;
                    structure.Add("await " + eventName);
                    shape.Append(" > await ").Append(eventName);
                    break;
                default:
                    throw new InvalidOperationException($"Unknown process node '{node.GetType().FullName}'. Update Wyrd.Tool.");
            }
        }

        if (process.GetType().GetProperty("Outcome")?.GetValue(process) is { } outcome)
        {
            var action = Required(outcome, "Action").ToString()!;
            structure.Add("outcome " + action);
            shape.Append(" ! ").Append(action);
        }

        var fingerprint = (string)Required(process, "Fingerprint");
        var expected = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(shape.ToString())))[..12];
        if (fingerprint != expected)
        {
            throw new InvalidOperationException($"Process '{member.Name}' uses an unsupported fingerprint format. Update Wyrd.Tool.");
        }

        return new ProcessSnapshot { Name = member.Name, Fingerprint = fingerprint, Structure = structure };
    }

    public static string Generate(IReadOnlyList<ProcessSnapshot> snapshots)
    {
        Validate(snapshots);
        var yaml = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .WithDefaultScalarStyle(ScalarStyle.DoubleQuoted)
            .WithIndentedSequences()
            .WithNewLine("\n")
            .DisableAliases()
            .Build()
            .Serialize(snapshots.OrderBy(p => p.Name, StringComparer.Ordinal).ToArray());
        return Header + "\n# Process structure only; retries and time limits are configuration. Review changes before regenerating.\n" + yaml;
    }

    public static IReadOnlyList<ProcessSnapshot> Parse(string text)
    {
        using var reader = new StringReader(text);
        if (reader.ReadLine() != Header)
        {
            throw new FormatException($"Unsupported process snapshot header. Expected '{Header}'.");
        }

        var snapshots = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .WithDuplicateKeyChecking()
            .Build()
            .Deserialize<List<ProcessSnapshot>>(reader) ?? throw new FormatException("Process snapshot must contain a YAML sequence (use [] for no processes).");
        Validate(snapshots);
        return snapshots;
    }

    public static ProcessCheck Check(IReadOnlyList<ProcessSnapshot> previous, IReadOnlyList<ProcessSnapshot> current)
    {
        Validate(previous);
        Validate(current);
        var old = previous.ToDictionary(p => p.Name, StringComparer.Ordinal);
        var next = current.ToDictionary(p => p.Name, StringComparer.Ordinal);
        var report = new StringBuilder();
        var changed = false;
        foreach (var name in old.Keys.Union(next.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            old.TryGetValue(name, out var before);
            next.TryGetValue(name, out var after);
            if (before is not null && after is not null && before.Fingerprint == after.Fingerprint
                && before.Structure.SequenceEqual(after.Structure, StringComparer.Ordinal))
            {
                continue;
            }

            changed = true;
            var kind = before is null ? "Added" : after is null ? "Removed" : "Changed";
            report.Append(kind).Append(" process: ").AppendLine(name);
            AppendStructure(report, "Old", before);
            AppendStructure(report, "New", after);
        }

        if (changed)
        {
            report.AppendLine("Changed definitions suspend in-flight instances of those processes (definition changed).");
            report.AppendLine("Deploy when none are running, or keep the old definition until they finish; then regenerate the snapshot with --output.");
            report.AppendLine("Added and removed processes also require snapshot review. Migration of in-flight instances is not available yet.");
        }
        else
        {
            report.AppendLine("Process snapshot matches. Retry and time-limit configuration is not checked.");
        }

        return new ProcessCheck(changed ? 1 : 0, report.ToString().ReplaceLineEndings("\n"));
    }

    private static object Required(object value, string property) => value.GetType().GetProperty(property)?.GetValue(value)
        ?? throw new InvalidOperationException($"Missing process metadata '{property}'. Update Wyrd.Tool.");

    private static void AppendStructure(StringBuilder report, string label, ProcessSnapshot? snapshot)
    {
        report.Append("  ").Append(label).Append(": ").AppendLine(snapshot?.Fingerprint ?? "(absent)");
        if (snapshot is not null)
        {
            foreach (var line in snapshot.Structure)
            {
                report.Append("    ").AppendLine(line);
            }
        }
    }

    private static void Validate(IReadOnlyList<ProcessSnapshot> snapshots)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var snapshot in snapshots)
        {
            if (snapshot is null || string.IsNullOrWhiteSpace(snapshot.Name)
                || snapshot.Fingerprint is not { Length: 12 } || !snapshot.Fingerprint.All(char.IsAsciiHexDigitLower)
                || snapshot.Structure is not { Count: > 0 } || snapshot.Structure.Any(string.IsNullOrWhiteSpace)
                || !snapshot.Structure[0].StartsWith("trigger ", StringComparison.Ordinal))
            {
                throw new FormatException("Invalid process snapshot: each process needs a name, a 12-character fingerprint and a structure starting with a trigger.");
            }

            if (!names.Add(snapshot.Name))
            {
                throw new FormatException($"Duplicate process name '{snapshot.Name}'. Process names must be unique.");
            }
        }
    }
}

internal sealed class ProcessSnapshot
{
    public string Name { get; set; } = "";
    public string Fingerprint { get; set; } = "";
    public List<string> Structure { get; set; } = [];
}

internal sealed record ProcessCheck(int ExitCode, string Report);
