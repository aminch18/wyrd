using System.Linq;
using Microsoft.CodeAnalysis;

namespace Wyrd.Analyzers;

internal sealed class WyrdSymbols
{
    private WyrdSymbols(
        INamedTypeSymbol command,
        INamedTypeSymbol @event,
        INamedTypeSymbol domainEvent,
        INamedTypeSymbol integrationEvent,
        INamedTypeSymbol integrationEventAttribute,
        INamedTypeSymbol? state,
        INamedTypeSymbol? slice)
    {
        Command = command;
        Event = @event;
        DomainEvent = domainEvent;
        IntegrationEvent = integrationEvent;
        IntegrationEventAttribute = integrationEventAttribute;
        State = state;
        Slice = slice;
    }

    public INamedTypeSymbol Command { get; }

    public INamedTypeSymbol Event { get; }

    public INamedTypeSymbol DomainEvent { get; }

    public INamedTypeSymbol IntegrationEvent { get; }

    public INamedTypeSymbol IntegrationEventAttribute { get; }

    public INamedTypeSymbol? State { get; }

    public INamedTypeSymbol? Slice { get; }

    public static WyrdSymbols? Resolve(Compilation compilation)
    {
        var command = compilation.GetTypeByMetadataName("Wyrd.ICommand");
        var @event = compilation.GetTypeByMetadataName("Wyrd.IEvent");
        var domainEvent = compilation.GetTypeByMetadataName("Wyrd.IDomainEvent");
        var integrationEvent = compilation.GetTypeByMetadataName("Wyrd.IIntegrationEvent");
        var attribute = compilation.GetTypeByMetadataName("Wyrd.IntegrationEventAttribute");

        return command is null || @event is null || domainEvent is null || integrationEvent is null || attribute is null
            ? null
            : new WyrdSymbols(
                command,
                @event,
                domainEvent,
                integrationEvent,
                attribute,
                compilation.GetTypeByMetadataName("Wyrd.IState"),
                compilation.GetTypeByMetadataName("Wyrd.Slice`4"));
    }

    public static bool Implements(ITypeSymbol type, INamedTypeSymbol @interface) =>
        SymbolEqualityComparer.Default.Equals(type, @interface)
        || type.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, @interface));

    /// <summary>Finds a domain type (command, domain event, state, slice) anywhere inside a property type.</summary>
    public ITypeSymbol? FindDomainType(ITypeSymbol type)
    {
        switch (type)
        {
            case IArrayTypeSymbol array:
                return FindDomainType(array.ElementType);
            case INamedTypeSymbol named:
                if (IsDomainType(named))
                {
                    return named;
                }

                foreach (var argument in named.TypeArguments)
                {
                    var found = FindDomainType(argument);
                    if (found is not null)
                    {
                        return found;
                    }
                }

                return null;
            default:
                return null;
        }
    }

    private bool IsDomainType(INamedTypeSymbol type)
    {
        if (Implements(type, Command) || Implements(type, DomainEvent) || (State is not null && Implements(type, State)))
        {
            return true;
        }

        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (Slice is not null && SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, Slice))
            {
                return true;
            }
        }

        return false;
    }
}
