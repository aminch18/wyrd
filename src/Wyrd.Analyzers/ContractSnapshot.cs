using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Wyrd.Analyzers;

/// <summary>
/// The schema of one integration event, as written in contracts.wyrd.txt:
/// <c>payments.payment-charged v1 | Amount: decimal; Currency: string; OrderId: string</c>.
/// </summary>
internal sealed class ContractSchema
{
    public ContractSchema(string name, int version, IReadOnlyDictionary<string, string> fields)
    {
        Name = name;
        Version = version;
        Fields = fields;
    }

    public string Name { get; }

    public int Version { get; }

    public IReadOnlyDictionary<string, string> Fields { get; }

    public string Id => Name + " v" + Version;

    public string ToLine() =>
        Id + " | " + string.Join("; ", Fields.OrderBy(f => f.Key, StringComparer.Ordinal).Select(f => f.Key + ": " + f.Value));

    public static bool IsOptional(string type) => type.EndsWith("?", StringComparison.Ordinal);
}

internal sealed class SnapshotEntry
{
    public SnapshotEntry(ContractSchema schema, bool retired, Location location)
    {
        Schema = schema;
        Retired = retired;
        Location = location;
    }

    public ContractSchema Schema { get; }

    public bool Retired { get; }

    public Location Location { get; }
}

internal sealed class ContractSnapshot
{
    public const string FileName = "contracts.wyrd.txt";

    private static readonly char[] Pipe = ['|'];
    private static readonly char[] Colon = [':'];

    private ContractSnapshot(bool exists, Dictionary<string, SnapshotEntry> entries)
    {
        Exists = exists;
        Entries = entries;
    }

    public bool Exists { get; }

    public Dictionary<string, SnapshotEntry> Entries { get; }

    public static ContractSnapshot Load(IEnumerable<AdditionalText> files)
    {
        var file = files.FirstOrDefault(f =>
            string.Equals(System.IO.Path.GetFileName(f.Path), FileName, StringComparison.OrdinalIgnoreCase));
        var entries = new Dictionary<string, SnapshotEntry>(StringComparer.Ordinal);
        var text = file?.GetText();
        if (file is null || text is null)
        {
            return new ContractSnapshot(false, entries);
        }

        foreach (var line in text.Lines)
        {
            var content = line.ToString().Trim();
            if (content.Length == 0 || content.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            var retired = content.StartsWith("retired ", StringComparison.Ordinal);
            if (retired)
            {
                content = content.Substring("retired ".Length).Trim();
            }

            var schema = Parse(content);
            if (schema is not null)
            {
                var location = Location.Create(file.Path, line.Span, text.Lines.GetLinePositionSpan(line.Span));
                entries[schema.Id] = new SnapshotEntry(schema, retired, location);
            }
        }

        return new ContractSnapshot(true, entries);
    }

    private static ContractSchema? Parse(string line)
    {
        var parts = line.Split(Pipe, 2);
        var head = parts[0].Trim().Split(' ');
        if (head.Length != 2 || !head[1].StartsWith("v", StringComparison.Ordinal)
            || !int.TryParse(head[1].Substring(1), out var version))
        {
            return null;
        }

        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        if (parts.Length == 2)
        {
            foreach (var field in parts[1].Split(';'))
            {
                var pair = field.Split(Colon, 2);
                if (pair.Length == 2)
                {
                    fields[pair[0].Trim()] = pair[1].Trim();
                }
            }
        }

        return new ContractSchema(head[0], version, fields);
    }
}
