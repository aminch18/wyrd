using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Wyrd.Runtime;

/// <summary>
/// Turns messages and states into (type name, JSON) and back. Integration events are named by their contract
/// (<c>payments.payment-charged v1</c>); everything else by its CLR type.
/// </summary>
internal sealed class MessageTypes
{
    private const string Justification =
        "Reflection-based JSON for now: only reachable through InMemoryRuntime, which is annotated. Source-generated contexts are on the roadmap.";

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.General);

    private readonly Dictionary<string, Type> _contracts = new(StringComparer.Ordinal);

    public string NameOf(Type type)
    {
        if (Contract.Find(type) is { } contract)
        {
            var name = contract.ToString();
            _contracts.TryAdd(name, type);
            return name;
        }

        return $"{type.FullName}, {type.Assembly.GetName().Name}";
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = Justification)]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = Justification)]
    public (string Type, string Payload) Serialize(object value) =>
        (NameOf(value.GetType()), JsonSerializer.Serialize(value, value.GetType(), Options));

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = Justification)]
    [UnconditionalSuppressMessage("Trimming", "IL2057", Justification = Justification)]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = Justification)]
    public object Deserialize(string type, string payload)
    {
        var target = _contracts.TryGetValue(type, out var contract)
            ? contract
            : Type.GetType(type, throwOnError: true)!;

        return JsonSerializer.Deserialize(payload, target, Options)
            ?? throw new InvalidOperationException($"'{type}' deserialized to null.");
    }

    [UnconditionalSuppressMessage("Trimming", "IL2057", Justification = Justification)]
    public Type TypeOf(string type) =>
        _contracts.TryGetValue(type, out var contract) ? contract : Type.GetType(type, throwOnError: true)!;

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = Justification)]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = Justification)]
    public static string SerializeReply(ReplyMessage reply) => JsonSerializer.Serialize(reply, Options);

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = Justification)]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = Justification)]
    public static ReplyMessage DeserializeReply(string payload) =>
        JsonSerializer.Deserialize<ReplyMessage>(payload, Options) ?? throw new InvalidOperationException("Empty reply.");
}
