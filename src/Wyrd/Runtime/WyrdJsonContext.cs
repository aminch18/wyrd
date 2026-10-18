using System.Text.Json.Serialization;

namespace Wyrd.Runtime;

/// <summary>Source-generated JSON for Wyrd's own messages.</summary>
[JsonSerializable(typeof(ReplyMessage))]
[JsonSerializable(typeof(Envelope))]
[JsonSerializable(typeof(StoredInstance))]
internal sealed partial class WyrdJsonContext : JsonSerializerContext;
