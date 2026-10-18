using System.Globalization;

namespace Wyrd.Tool;

internal sealed record ContractField(string Name, string Type);

internal sealed record Contract(string Name, int Version, bool Retired, IReadOnlyList<ContractField> Fields)
{
    public string MessageName => $"{Name}.v{Version}";
}

internal static class Contracts
{
    public static IReadOnlyList<Contract> Parse(string snapshot)
    {
        var contracts = new List<Contract>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        using var reader = new StringReader(snapshot);
        string? line;
        var number = 0;
        while ((line = reader.ReadLine()) is not null)
        {
            number++;
            var content = line.Trim();
            if (content.Length == 0 || content.StartsWith('#'))
            {
                continue;
            }

            var retired = content.StartsWith("retired ", StringComparison.Ordinal);
            if (retired)
            {
                content = content["retired ".Length..].Trim();
            }

            var parts = content.Split('|');
            if (parts.Length is < 1 or > 2)
            {
                throw Invalid(number, "expected 'name vN | Field: type; ...'");
            }

            var head = parts[0].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (head.Length != 2 || head[0].Length == 0 || !head[1].StartsWith('v')
                || !int.TryParse(head[1].AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var version)
                || version < 1)
            {
                throw Invalid(number, "expected a contract name followed by a positive version (v1, v2, ...)");
            }

            var fields = new List<ContractField>();
            var fieldNames = new HashSet<string>(StringComparer.Ordinal);
            if (parts.Length == 2 && parts[1].Trim().Length > 0)
            {
                foreach (var field in parts[1].Split(';'))
                {
                    var colon = field.IndexOf(':');
                    if (colon <= 0 || colon == field.Length - 1 || field.IndexOf(':', colon + 1) >= 0)
                    {
                        throw Invalid(number, "expected each field as 'Name: type'");
                    }

                    var name = field[..colon].Trim();
                    var type = field[(colon + 1)..].Trim();
                    if (name.Length == 0 || type.Length == 0 || !fieldNames.Add(name))
                    {
                        throw Invalid(number, $"empty or duplicate field '{name}'");
                    }

                    fields.Add(new ContractField(name, type));
                }
            }

            var contract = new Contract(head[0], version, retired, fields);
            if (!seen.Add(contract.MessageName))
            {
                throw Invalid(number, $"duplicate contract '{contract.MessageName}'");
            }

            contracts.Add(contract);
        }

        return contracts.OrderBy(c => c.Name, StringComparer.Ordinal).ThenBy(c => c.Version).ToArray();
    }

    private static FormatException Invalid(int line, string reason) => new($"Invalid contract snapshot at line {line}: {reason}.");
}
