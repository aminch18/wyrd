using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace Wyrd.Tool;

internal sealed record SchemaType(
    [property: YamlMember(Order = 0, ScalarStyle = ScalarStyle.Plain)] string Type,
    [property: YamlMember(Order = 1, ScalarStyle = ScalarStyle.Plain)] string? Format = null,
    [property: YamlMember(Order = 2)] string? Description = null,
    [property: YamlMember(Order = 3)] SchemaType? Items = null)
{
    private static readonly string[] CollectionTypes =
        ["IReadOnlyList<", "List<", "ImmutableList<", "ImmutableArray<", "IReadOnlyCollection<"];

    public static SchemaType Parse(string snapshotType)
    {
        var type = snapshotType.Trim().TrimEnd('?').TrimEnd();
        if (type.EndsWith("[]", StringComparison.Ordinal))
        {
            return new SchemaType("array", Items: Parse(type[..^2]));
        }

        foreach (var collection in CollectionTypes)
        {
            if (type.StartsWith(collection, StringComparison.Ordinal) && type.EndsWith('>'))
            {
                return new SchemaType("array", Items: Parse(type[collection.Length..^1]));
            }
        }

        return type switch
        {
            "string" => new("string"),
            "byte" or "sbyte" or "short" or "ushort" or "int" or "uint" or "long" or "ulong" => new("integer"),
            "decimal" or "double" or "float" => new("number"),
            "bool" => new("boolean"),
            "Guid" => new("string", "uuid"),
            "DateOnly" => new("string", "date"),
            "DateTime" or "DateTimeOffset" => new("string", "date-time"),
            "TimeSpan" => new("string", "duration"),
            _ => new("object", Description: type),
        };
    }
}
