using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Wyrd.Analyzers;

/// <summary>Suggests ubiquitous-language names: commands in the imperative, events in the past tense.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NamingAnalyzer : DiagnosticAnalyzer
{
    private static readonly Regex WordBoundary = new("(?<!^)(?=[A-Z])", RegexOptions.Compiled);
    private static readonly Regex VersionSuffix = new("V[0-9]+$", RegexOptions.Compiled);

    private static readonly HashSet<string> IrregularPastTense = new(StringComparer.Ordinal)
    {
        "Bought", "Brought", "Built", "Caught", "Chosen", "Cut", "Done", "Drawn", "Found", "Frozen", "Given",
        "Held", "Hidden", "Kept", "Left", "Lent", "Lost", "Made", "Met", "Paid", "Put", "Read", "Rebuilt",
        "Reset", "Sent", "Set", "Shut", "Sold", "Spent", "Split", "Taken", "Thought", "Undone", "Withdrawn",
        "Won", "Written",
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Diagnostics.EventNaming, Diagnostics.CommandNaming);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var symbols = WyrdSymbols.Resolve(start.Compilation);
            if (symbols is null)
            {
                return;
            }

            start.RegisterSymbolAction(c => Analyze(c, symbols), SymbolKind.NamedType);
        });
    }

    internal static bool LooksPastTense(string word) =>
        IrregularPastTense.Contains(word) || (word.Length > 3 && word.EndsWith("ed", StringComparison.Ordinal));

    private static void Analyze(SymbolAnalysisContext context, WyrdSymbols symbols)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (type.IsAbstract || type.TypeKind == TypeKind.Interface)
        {
            return;
        }

        var isEvent = WyrdSymbols.Implements(type, symbols.Event);
        var isCommand = WyrdSymbols.Implements(type, symbols.Command);
        if (isEvent == isCommand)
        {
            return; // Not a message, or both (already WYRD0001).
        }

        var last = WordBoundary.Split(VersionSuffix.Replace(type.Name, string.Empty)).LastOrDefault() ?? string.Empty;
        var location = type.Locations.FirstOrDefault();

        if (isEvent && !LooksPastTense(last))
        {
            context.ReportDiagnostic(Diagnostic.Create(Diagnostics.EventNaming, location, type.Name));
        }
        else if (isCommand && LooksPastTense(last))
        {
            context.ReportDiagnostic(Diagnostic.Create(Diagnostics.CommandNaming, location, type.Name, last));
        }
    }
}
