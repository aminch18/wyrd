using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace Wyrd.Tool;

internal sealed record AsyncApiDocument(
    [property: YamlMember(Alias = "asyncapi", Order = 0, ScalarStyle = ScalarStyle.Plain)] string AsyncApiVersion,
    [property: YamlMember(Order = 1)] ApiInfo Info,
    [property: YamlMember(Order = 2)] IReadOnlyDictionary<string, AsyncApiChannel> Channels,
    [property: YamlMember(Order = 3)] ApiComponents Components);

internal sealed record ApiInfo(
    [property: YamlMember(Order = 0)] string Title,
    [property: YamlMember(Order = 1)] string Version);

internal sealed record AsyncApiChannel(
    [property: YamlMember(Order = 0)] string Address,
    [property: YamlMember(Order = 1)] IReadOnlyDictionary<string, MessageReference> Messages);

internal sealed record MessageReference([property: YamlMember(Alias = "$ref")] string Reference);

internal sealed record ApiComponents(IReadOnlyDictionary<string, AsyncApiMessage> Messages);

internal sealed record AsyncApiMessage(
    [property: YamlMember(Order = 0)] string Name,
    [property: YamlMember(Order = 1)] PayloadSchema Payload);

internal sealed record PayloadSchema(
    [property: YamlMember(Order = 0, ScalarStyle = ScalarStyle.Plain)] string Type,
    [property: YamlMember(Order = 1)] bool? Deprecated,
    [property: YamlMember(Order = 2)] IReadOnlyDictionary<string, SchemaType> Properties,
    [property: YamlMember(Order = 3)] IReadOnlyList<string>? Required);
