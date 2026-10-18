using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Wyrd.Tool;

internal static class AsyncApi
{
    public static string Generate(IReadOnlyList<Contract> contracts, string title, string version)
    {
        var document = CreateDocument(contracts, title, version);
        return new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .WithDefaultScalarStyle(ScalarStyle.DoubleQuoted)
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
            .WithIndentedSequences()
            .WithNewLine("\n")
            .DisableAliases()
            .Build()
            .Serialize(document);
    }

    internal static AsyncApiDocument CreateDocument(IReadOnlyList<Contract> contracts, string title, string version)
    {
        var ordered = contracts.OrderBy(c => c.Name, StringComparer.Ordinal).ThenBy(c => c.Version).ToArray();
        var channels = ordered.GroupBy(c => c.Name, StringComparer.Ordinal).ToDictionary(
            group => group.Key,
            CreateChannel,
            StringComparer.Ordinal);
        var messages = ordered.ToDictionary(
            contract => contract.MessageName,
            contract => new AsyncApiMessage(contract.MessageName, CreatePayload(contract)),
            StringComparer.Ordinal);

        return new AsyncApiDocument("3.0.0", new ApiInfo(title, version), channels, new ApiComponents(messages));
    }

    private static AsyncApiChannel CreateChannel(IGrouping<string, Contract> contracts)
    {
        var messages = contracts.ToDictionary(
            contract => $"v{contract.Version}",
            contract => new MessageReference($"#/components/messages/{EscapePointer(contract.MessageName)}"),
            StringComparer.Ordinal);
        return new AsyncApiChannel(contracts.Key, messages);
    }

    private static PayloadSchema CreatePayload(Contract contract)
    {
        var fields = contract.Fields.OrderBy(field => field.Name, StringComparer.Ordinal).ToArray();
        var properties = fields.ToDictionary(field => field.Name, field => SchemaType.Parse(field.Type), StringComparer.Ordinal);
        var required = fields.Where(field => !field.Type.EndsWith('?')).Select(field => field.Name).ToArray();
        return new PayloadSchema("object", contract.Retired ? true : null, properties, required.Length == 0 ? null : required);
    }

    private static string EscapePointer(string value) =>
        value.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
}
