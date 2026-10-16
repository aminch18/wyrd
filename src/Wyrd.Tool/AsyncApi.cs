using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Wyrd.Tool;

internal static class AsyncApi
{
    public static string Generate(IReadOnlyList<Contract> contracts, string title, string version)
    {
        var yaml = new StringBuilder();
        yaml.AppendLine("asyncapi: 3.0.0");
        yaml.AppendLine("info:");
        yaml.AppendLine(CultureInfo.InvariantCulture, $"  title: {Quote(title)}");
        yaml.AppendLine(CultureInfo.InvariantCulture, $"  version: {Quote(version)}");

        var groups = contracts.GroupBy(c => c.Name, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal).ToArray();
        yaml.AppendLine(groups.Length == 0 ? "channels: {}" : "channels:");
        foreach (var group in groups)
        {
            yaml.AppendLine(CultureInfo.InvariantCulture, $"  {Quote(group.Key)}:");
            yaml.AppendLine(CultureInfo.InvariantCulture, $"    address: {Quote(group.Key)}");
            yaml.AppendLine("    messages:");
            foreach (var contract in group.OrderBy(c => c.Version))
            {
                yaml.AppendLine(CultureInfo.InvariantCulture, $"      {Quote($"v{contract.Version}")}:");
                yaml.AppendLine(CultureInfo.InvariantCulture, $"        $ref: {Quote($"#/components/messages/{contract.MessageName}")}");
            }
        }

        yaml.AppendLine("components:");
        yaml.AppendLine(contracts.Count == 0 ? "  messages: {}" : "  messages:");
        foreach (var contract in contracts.OrderBy(c => c.Name, StringComparer.Ordinal).ThenBy(c => c.Version))
        {
            yaml.AppendLine(CultureInfo.InvariantCulture, $"    {Quote(contract.MessageName)}:");
            yaml.AppendLine(CultureInfo.InvariantCulture, $"      name: {Quote(contract.MessageName)}");
            yaml.AppendLine("      payload:");
            yaml.AppendLine("        type: object");
            if (contract.Retired)
            {
                yaml.AppendLine("        deprecated: true");
            }
            if (contract.Fields.Count == 0)
            {
                yaml.AppendLine("        properties: {}");
                continue;
            }

            yaml.AppendLine("        properties:");
            foreach (var field in contract.Fields.OrderBy(f => f.Name, StringComparer.Ordinal))
            {
                yaml.AppendLine(CultureInfo.InvariantCulture, $"          {Quote(field.Name)}:");
                WriteType(yaml, field.Type.TrimEnd('?'), 12);
            }

            var required = contract.Fields.Where(f => !f.Type.EndsWith('?'))
                .Select(f => f.Name).Order(StringComparer.Ordinal).ToArray();
            if (required.Length > 0)
            {
                yaml.AppendLine("        required:");
                foreach (var field in required)
                {
                    yaml.AppendLine(CultureInfo.InvariantCulture, $"          - {Quote(field)}");
                }
            }
        }

        return yaml.ToString();
    }

    private static void WriteType(StringBuilder yaml, string type, int indent)
    {
        var prefix = new string(' ', indent);
        if (type.EndsWith("[]", StringComparison.Ordinal))
        {
            WriteArray(yaml, type[..^2], indent);
            return;
        }

        foreach (var collection in new[] { "IReadOnlyList<", "List<" })
        {
            if (type.StartsWith(collection, StringComparison.Ordinal) && type.EndsWith('>'))
            {
                WriteArray(yaml, type[collection.Length..^1].Trim(), indent);
                return;
            }
        }

        var (schemaType, format) = type switch
        {
            "string" => ("string", ""),
            "int" or "long" or "short" => ("integer", ""),
            "decimal" or "double" or "float" => ("number", ""),
            "bool" => ("boolean", ""),
            "Guid" => ("string", "uuid"),
            "DateOnly" => ("string", "date"),
            "DateTime" or "DateTimeOffset" => ("string", "date-time"),
            "TimeSpan" => ("string", "duration"),
            _ => ("object", ""),
        };
        yaml.AppendLine(CultureInfo.InvariantCulture, $"{prefix}type: {schemaType}");
        if (format.Length > 0)
        {
            yaml.AppendLine(CultureInfo.InvariantCulture, $"{prefix}format: {format}");
        }
        if (schemaType == "object")
        {
            yaml.AppendLine(CultureInfo.InvariantCulture, $"{prefix}description: {Quote(type)}");
        }
    }

    private static void WriteArray(StringBuilder yaml, string elementType, int indent)
    {
        yaml.AppendLine(CultureInfo.InvariantCulture, $"{new string(' ', indent)}type: array");
        yaml.AppendLine(CultureInfo.InvariantCulture, $"{new string(' ', indent)}items:");
        WriteType(yaml, elementType, indent + 2);
    }

    private static string Quote(string value) => JsonSerializer.Serialize(value);
}
