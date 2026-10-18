using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Wyrd.Analyzers;

/// <summary>Reports commands decided by more than one slice in the same compilation.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SliceAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Diagnostics.MultipleOwners);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var slice = start.Compilation.GetTypeByMetadataName("Wyrd.Slice`4");
            if (slice is null)
            {
                return;
            }

            var owners = new ConcurrentBag<(ITypeSymbol Command, INamedTypeSymbol Slice)>();

            start.RegisterSymbolAction(c =>
            {
                var type = (INamedTypeSymbol)c.Symbol;
                for (var current = type.BaseType; current is not null; current = current.BaseType)
                {
                    if (!type.IsAbstract && SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, slice))
                    {
                        owners.Add((current.TypeArguments[0], type));
                        return;
                    }
                }
            }, SymbolKind.NamedType);

            start.RegisterCompilationEndAction(end =>
            {
                foreach (var group in owners.GroupBy(o => o.Command, SymbolEqualityComparer.Default).Where(g => g.Count() > 1))
                {
                    var names = string.Join(", ", group.Select(o => o.Slice.Name).OrderBy(n => n, System.StringComparer.Ordinal));
                    foreach (var owner in group)
                    {
                        end.ReportDiagnostic(Diagnostic.Create(
                            Diagnostics.MultipleOwners, owner.Slice.Locations.FirstOrDefault(), group.Key!.Name, names));
                    }
                }
            });
        });
    }
}
