using System.Text.Json.Serialization;

namespace Wyrd.Runtime;

/// <summary>Source-generated JSON for Wyrd's own messages.</summary>
[JsonSerializable(typeof(Envelope))]
[JsonSerializable(typeof(StoredInstance))]
[JsonSerializable(typeof(ReplyMessage))]
[JsonSerializable(typeof(InstanceHistory))]
[JsonSourceGenerationOptions(WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault)]
internal sealed partial class WyrdJsonContext : JsonSerializerContext;
