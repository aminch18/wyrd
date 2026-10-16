using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Wyrd.Analyzers;

/// <summary>
/// When the application opts into source-generated JSON with a [WyrdJsonContext], checks that every type Wyrd
/// serializes is declared in it, and that every case of a [Union] is declared as a JSON derived type.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class JsonContextAnalyzer : DiagnosticAnalyzer
{
    private static readonly SymbolDisplayFormat Qualified = SymbolDisplayFormat.FullyQualifiedFormat;

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Diagnostics.MissingFromJsonContext, Diagnostics.UnionCaseNotDerived);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(OnCompilationStart);
    }

    private static void OnCompilationStart(CompilationStartAnalysisContext start)
    {
        var compilation = start.Compilation;
        var marker = compilation.GetTypeByMetadataName("Wyrd.WyrdJsonContextAttribute");
        var serializable = compilation.GetTypeByMetadataName("System.Text.Json.Serialization.JsonSerializableAttribute");
        var derived = compilation.GetTypeByMetadataName("System.Text.Json.Serialization.JsonDerivedTypeAttribute");
        var union = compilation.GetTypeByMetadataName("Wyrd.UnionAttribute");
        var symbols = WyrdSymbols.Resolve(compilation);
        if (marker is null || serializable is null || derived is null || symbols is null)
        {
            return;
        }

        var contexts = new ConcurrentBag<INamedTypeSymbol>();
        var unions = new ConcurrentBag<INamedTypeSymbol>();
        var required = new ConcurrentDictionary<ITypeSymbol, Location?>(SymbolEqualityComparer.Default);
        var slice = compilation.GetTypeByMetadataName("Wyrd.Slice`4");
        var projection = compilation.GetTypeByMetadataName("Wyrd.Projection`1");

        start.RegisterSymbolAction(c =>
        {
            var type = (INamedTypeSymbol)c.Symbol;
            var location = type.Locations.FirstOrDefault();

            if (type.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, marker)))
            {
                contexts.Add(type);
            }

            if (!type.IsAbstract && (WyrdSymbols.Implements(type, symbols.Command) || WyrdSymbols.Implements(type, symbols.Event))
                && !IsUnionCase(type, union))
            {
                required.TryAdd(type, location);
            }

            for (var current = type.BaseType; current is not null; current = current.BaseType)
            {
                var definition = current.OriginalDefinition;
                if (SymbolEqualityComparer.Default.Equals(definition, slice))
                {
                    foreach (var argument in current.TypeArguments)
                    {
                        required.TryAdd(argument, location);
                    }
                }
                else if (SymbolEqualityComparer.Default.Equals(definition, projection))
                {
                    required.TryAdd(current.TypeArguments[0], location);
                }
            }

            if (union is not null && type.IsAbstract
                && type.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, union)))
            {
                unions.Add(type);
            }
        }, SymbolKind.NamedType);

        start.RegisterCompilationEndAction(end =>
        {
            var declaredIn = contexts.OrderBy(c => c.Name, StringComparer.Ordinal).FirstOrDefault();

            // With a context, every union must declare its cases. Without one (reflection mode), Wyrd discovers them —
            // unless the union already declares some, which turns discovery off: then it must declare all.
            foreach (var type in unions)
            {
                CheckUnionCases(end, type, derived, mustDeclare: declaredIn is not null);
            }

            if (declaredIn is null)
            {
                return; // Reflection mode: types don't need declaring.
            }

            var declared = new HashSet<ITypeSymbol>(
                contexts.SelectMany(c => c.GetAttributes())
                    .Where(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, serializable))
                    .Select(a => a.ConstructorArguments.FirstOrDefault().Value)
                    .OfType<ITypeSymbol>(),
                SymbolEqualityComparer.Default);

            foreach (var pair in required.OrderBy(p => p.Key.ToDisplayString(), StringComparer.Ordinal))
            {
                if (declared.Contains(pair.Key) || pair.Key.TypeKind == TypeKind.TypeParameter)
                {
                    continue;
                }

                var properties = ImmutableDictionary<string, string?>.Empty.Add(JsonContextFix.TypeProperty, pair.Key.ToDisplayString(Qualified));
                end.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.MissingFromJsonContext,
                    pair.Value,
                    additionalLocations: declaredIn.Locations,
                    properties: properties,
                    pair.Key.Name,
                    declaredIn.Name));
            }
        });
    }

    private static void CheckUnionCases(CompilationAnalysisContext context, INamedTypeSymbol union, INamedTypeSymbol derivedAttribute, bool mustDeclare)
    {
        var declared = new HashSet<ITypeSymbol>(
            union.GetAttributes()
                .Where(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, derivedAttribute))
                .Select(a => a.ConstructorArguments.FirstOrDefault().Value)
                .OfType<ITypeSymbol>(),
            SymbolEqualityComparer.Default);

        if (!mustDeclare && declared.Count == 0)
        {
            return;
        }

        foreach (var @case in union.GetTypeMembers().Where(t => !t.IsAbstract && Inherits(t, union)))
        {
            if (!declared.Contains(@case))
            {
                var properties = ImmutableDictionary<string, string?>.Empty
                    .Add(JsonContextFix.TypeProperty, @case.ToDisplayString(Qualified))
                    .Add(JsonContextFix.CaseProperty, @case.Name);
                context.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.UnionCaseNotDerived, union.Locations.FirstOrDefault(), properties, union.Name, @case.Name));
            }
        }
    }

    private static bool IsUnionCase(INamedTypeSymbol type, INamedTypeSymbol? union) =>
        union is not null && type.ContainingType is { } container && Inherits(type, container)
        && container.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, union));

    private static bool Inherits(INamedTypeSymbol type, INamedTypeSymbol baseType)
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
