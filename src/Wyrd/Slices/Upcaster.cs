using System.Collections.Concurrent;
using System.Reflection;

namespace Wyrd;

/// <summary>
/// Converts an integration event contract to its next version, so subscribers can move to v(n+1)
/// while publishers still emit v(n). Upcasters chain: v1 -> v2 -> v3.
/// </summary>
public abstract class Upcaster<TFrom, TTo> : IUpcasterHandler
    where TFrom : IIntegrationEvent
    where TTo : IIntegrationEvent
{
    public abstract TTo Map(TFrom previous);

    Contract IUpcasterHandler.From => Contract.Of(typeof(TFrom));

    Contract IUpcasterHandler.To => Contract.Of(typeof(TTo));

    IIntegrationEvent IUpcasterHandler.Map(IIntegrationEvent previous) => Map((TFrom)previous);
}

internal interface IUpcasterHandler
{
    Contract From { get; }

    Contract To { get; }

    IIntegrationEvent Map(IIntegrationEvent previous);
}

/// <summary>The identity of an integration event contract: its name and version.</summary>
internal sealed record Contract(string Name, int Version, Type Type)
{
    private static readonly ConcurrentDictionary<Type, Contract?> Cache = new();

    public static Contract Of(Type type) =>
        Find(type) ?? throw new InvalidOperationException(
            $"{type.Name} is not an integration event contract: mark it with [IntegrationEvent(\"context.name\", Version = n)].");

    public static Contract? Find(Type type) =>
        Cache.GetOrAdd(type, static t =>
            typeof(IIntegrationEvent).IsAssignableFrom(t) && t.GetCustomAttribute<IntegrationEventAttribute>() is { } attribute
                ? new Contract(attribute.Name, attribute.Version, t)
                : null);

    public override string ToString() => $"{Name} v{Version}";
}
