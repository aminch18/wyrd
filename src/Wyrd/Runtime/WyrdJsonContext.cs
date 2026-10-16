using System.Text.Json.Serialization;

namespace Wyrd.Runtime;

/// <summary>Source-generated JSON for Wyrd's own messages.</summary>
[JsonSerializable(typeof(ReplyMessage))]
internal sealed partial class WyrdJsonContext : JsonSerializerContext;
