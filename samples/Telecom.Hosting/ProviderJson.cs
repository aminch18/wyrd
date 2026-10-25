using System.Text.Json.Serialization;

namespace Telecom.Hosting;

// HTTP uses ASP.NET's camel-case convention; Wyrd's stored JSON keeps its own metadata unchanged.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(ProviderRequest))]
[JsonSerializable(typeof(ProviderReply))]
internal sealed partial class ProviderJson : JsonSerializerContext;
