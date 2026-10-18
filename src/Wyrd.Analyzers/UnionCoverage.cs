using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Wyrd.Analyzers;

/// <summary>Which cases of a [Union] a switch expression handles.</summary>
internal static class UnionCoverage
{
    /// <summary>
    /// For a switch expression over a [Union], the cases it doesn't handle (empty when exhaustive); null when the
    /// switch is not over a union. Arms with a guard (<c>when</c>) or with property conditions don't count as
    /// handling a case; <c>_</c> and <c>var x</c> handle everything.
    /// </summary>
    public static (INamedTypeSymbol Union, IReadOnlyList<INamedTypeSymbol> Missing)? Analyze(
        ISwitchExpressionOperation switchExpression, INamedTypeSymbol unionAttribute)
    {
        if (switchExpression.Value.Type is not INamedTypeSymbol union || !union.IsAbstract
            || !union.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, unionAttribute)))
        {
            return null;
        }

        var handled = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var arm in switchExpression.Arms.Where(a => a.Guard is null))
        {
            foreach (var type in Handled(arm.Pattern, union))
            {
                handled.Add(type);
            }
        }

        var missing = union.GetTypeMembers()
            .Where(c => !c.IsAbstract && Inherits(c, union))
            .Where(c => !handled.Any(h => SymbolEqualityComparer.Default.Equals(h, c) || Inherits(c, h)))
            .ToList();

        return (union, missing);
    }

    private static IEnumerable<ITypeSymbol> Handled(IPatternOperation pattern, INamedTypeSymbol union) => pattern switch
    {
        IDiscardPatternOperation => [union],
        IDeclarationPatternOperation { MatchesNull: true } => [union],
        IDeclarationPatternOperation declaration when declaration.MatchedType is not null => [declaration.MatchedType],
        ITypePatternOperation type => [type.MatchedType],
        IRecursivePatternOperation recursive when recursive.MatchedType is not null
            && recursive.DeconstructionSubpatterns.All(p => p is IDiscardPatternOperation)
            && recursive.PropertySubpatterns.All(p => p.Pattern is IDiscardPatternOperation) => [recursive.MatchedType],
        IBinaryPatternOperation { OperatorKind: BinaryOperatorKind.Or } or => Handled(or.LeftPattern, union).Concat(Handled(or.RightPattern, union)),
        _ => [],
    };

    private static bool Inherits(ITypeSymbol type, ITypeSymbol baseType)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, baseType))
            {
                return true;
            }
        }

        return false;
    }
}
