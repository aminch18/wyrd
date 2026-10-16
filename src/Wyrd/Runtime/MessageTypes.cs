using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Wyrd.Runtime;

/// <summary>
/// Turns messages, states and views into (type name, JSON) and back. Types are resolved by registered name —
/// never <c>Type.GetType</c> — and serialized as their declared type, so unions are stored polymorphically.
/// Two modes: source-generated (your <see cref="JsonSerializerContext"/>, AOT-safe) or reflection (development).
/// </summary>
internal sealed class MessageTypes
{
    private const string ReflectionReason = "Reflection-based System.Text.Json. Pass a [WyrdJsonContext] for AOT and trimming.";

    private readonly JsonSerializerOptions _options;
    private readonly Dictionary<string, Type> _byName = new(StringComparer.Ordinal);

    private MessageTypes(JsonSerializerOptions options) => _options = options;

    /// <summary>Source-generated metadata from the application's context: no reflection, AOT-safe.</summary>
    public static MessageTypes FromContext(IJsonTypeInfoResolver context) => new(new JsonSerializerOptions { TypeInfoResolver = context });

    /// <summary>Reflection metadata, with [Union] records made polymorphic automatically. For development and tests.</summary>
    [RequiresUnreferencedCode(ReflectionReason)]
    [RequiresDynamicCode(ReflectionReason)]
    public static MessageTypes Reflection() =>
        new(new JsonSerializerOptions { TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { UnionPolymorphism } } });

    public static string NameOf(Type type) => Contract.Find(type)?.ToString() ?? type.FullName ?? type.Name;

    public void Register(Type type)
    {
        var name = NameOf(type);
        if (_byName.TryGetValue(name, out var existing) && existing != type)
        {
            throw new InvalidOperationException($"Two types share the message name '{name}': {existing} and {type}.");
        }

        _byName[name] = type;
    }

    /// <summary>Serializes <paramref name="value"/> as <paramref name="declared"/> (e.g. a union base).</summary>
    public (string Type, string Payload) Serialize(object value, Type declared)
    {
        Register(declared);
        return (NameOf(declared), JsonSerializer.Serialize(value, TypeInfo(declared)));
    }

    public object Deserialize(string type, string payload) =>
        JsonSerializer.Deserialize(payload, TypeInfo(TypeOf(type)))
            ?? throw new InvalidOperationException($"'{type}' deserialized to null.");

    public Type TypeOf(string type) =>
        _byName.TryGetValue(type, out var known)
            ? known
            : throw new InvalidOperationException(
                $"Unknown message type '{type}'. Register the slice, publication, projection or process that uses it.");

    public static string SerializeReply(ReplyMessage reply) => JsonSerializer.Serialize(reply, WyrdJsonContext.Default.ReplyMessage);

    public static ReplyMessage DeserializeReply(string payload) =>
        JsonSerializer.Deserialize(payload, WyrdJsonContext.Default.ReplyMessage) ?? throw new InvalidOperationException("Empty reply.");

    private JsonTypeInfo TypeInfo(Type type)
    {
        try
        {
            return _options.GetTypeInfo(type);
        }
        catch (NotSupportedException ex)
        {
            throw new InvalidOperationException(
                $"{type.Name} has no JSON metadata. Add [JsonSerializable(typeof({type.Name}))] to your [WyrdJsonContext].", ex);
        }
    }

    /// <summary>[Union] records become polymorphic: each nested case is a derived type, named after the case.</summary>
    [RequiresUnreferencedCode(ReflectionReason)]
    private static void UnionPolymorphism(JsonTypeInfo info)
    {
        if (!info.Type.IsAbstract || info.PolymorphismOptions is not null || info.Type.GetCustomAttribute<UnionAttribute>() is null)
        {
            return;
        }

        var polymorphism = new JsonPolymorphismOptions();
        foreach (var @case in info.Type.GetNestedTypes().Where(t => !t.IsAbstract && info.Type.IsAssignableFrom(t)))
        {
            polymorphism.DerivedTypes.Add(new JsonDerivedType(@case, @case.Name));
        }

        info.PolymorphismOptions = polymorphism;
    }
}
