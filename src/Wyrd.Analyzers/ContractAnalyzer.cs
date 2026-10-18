using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Wyrd.Analyzers;

/// <summary>
/// Enforces the message rules (commands vs events, no domain types in contracts) and checks every
/// integration event against the contract snapshot in contracts.wyrd.txt.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ContractAnalyzer : DiagnosticAnalyzer
{
    private static readonly SymbolDisplayFormat TypeFormat = SymbolDisplayFormat.MinimallyQualifiedFormat
        .AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(
        Diagnostics.CommandAndEvent,
        Diagnostics.LeaksDomainType,
        Diagnostics.IncompleteIntegrationEvent,
        Diagnostics.NotInSnapshot,
        Diagnostics.BreakingChange,
        Diagnostics.SnapshotOutdated,
        Diagnostics.ContractRemoved,
        Diagnostics.DuplicateContract);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(OnCompilationStart);
    }

    private static void OnCompilationStart(CompilationStartAnalysisContext context)
    {
        var symbols = WyrdSymbols.Resolve(context.Compilation);
        if (symbols is null)
        {
            return; // Wyrd is not referenced.
        }

        var snapshot = ContractSnapshot.Load(context.Options.AdditionalFiles);
        var found = new ConcurrentBag<(ContractSchema Schema, INamedTypeSymbol Type)>();

        context.RegisterSymbolAction(c => AnalyzeType(c, symbols, snapshot, found), SymbolKind.NamedType);
        context.RegisterCompilationEndAction(c => AnalyzeCompilation(c, snapshot, found));
    }

    private static void AnalyzeType(
        SymbolAnalysisContext context,
        WyrdSymbols symbols,
        ContractSnapshot snapshot,
        ConcurrentBag<(ContractSchema, INamedTypeSymbol)> found)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        var location = type.Locations.FirstOrDefault();

        if (WyrdSymbols.Implements(type, symbols.Command) && WyrdSymbols.Implements(type, symbols.Event))
        {
            context.ReportDiagnostic(Diagnostic.Create(Diagnostics.CommandAndEvent, location, type.Name));
        }

        var attribute = type.GetAttributes().FirstOrDefault(a =>
            SymbolEqualityComparer.Default.Equals(a.AttributeClass, symbols.IntegrationEventAttribute));
        var isIntegrationEvent = WyrdSymbols.Implements(type, symbols.IntegrationEvent);

        if (type.IsAbstract || type.TypeKind == TypeKind.Interface)
        {
            return;
        }

        if (attribute is null && isIntegrationEvent)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.IncompleteIntegrationEvent, location, type.Name, "implements IIntegrationEvent but has no [IntegrationEvent] name and version"));
            return;
        }

        if (attribute is not null && !isIntegrationEvent)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.IncompleteIntegrationEvent, location, type.Name, "has [IntegrationEvent] but does not implement IIntegrationEvent"));
            return;
        }

        if (attribute is null)
        {
            return;
        }

        var properties = type.GetMembers().OfType<IPropertySymbol>()
            .Where(p => p.DeclaredAccessibility == Accessibility.Public && !p.IsStatic && !p.IsIndexer && p.GetMethod is not null)
            .ToList();

        foreach (var property in properties)
        {
            var leaked = symbols.FindDomainType(property.Type);
            if (leaked is not null)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.LeaksDomainType, property.Locations.FirstOrDefault() ?? location, type.Name, leaked.Name, property.Name));
            }
        }

        var schema = new ContractSchema(
            ReadName(attribute) ?? type.Name,
            ReadVersion(attribute),
            properties.ToDictionary(p => p.Name, p => p.Type.ToDisplayString(TypeFormat), StringComparer.Ordinal));

        found.Add((schema, type));
        CompareWithSnapshot(context, snapshot, schema, location);
    }

    private static void CompareWithSnapshot(SymbolAnalysisContext context, ContractSnapshot snapshot, ContractSchema schema, Location? location)
    {
        var fix = ImmutableDictionary<string, string?>.Empty
            .Add(SnapshotFix.LineProperty, schema.ToLine())
            .Add(SnapshotFix.IdProperty, schema.Id);

        if (!snapshot.Entries.TryGetValue(schema.Id, out var entry))
        {
            context.ReportDiagnostic(Diagnostic.Create(Diagnostics.NotInSnapshot, location, fix, schema.Name, schema.Version, schema.ToLine()));
            return;
        }

        var before = entry.Schema.Fields;
        var after = schema.Fields;
        var breaking = new List<string>();

        foreach (var field in before)
        {
            if (!after.TryGetValue(field.Key, out var type))
            {
                breaking.Add($"field '{field.Key}' was removed or renamed");
            }
            else if (!string.Equals(type, field.Value, StringComparison.Ordinal))
            {
                breaking.Add($"field '{field.Key}' changed type from {field.Value} to {type}");
            }
        }

        var added = after.Where(f => !before.ContainsKey(f.Key)).ToList();
        breaking.AddRange(added.Where(f => !ContractSchema.IsOptional(f.Value)).Select(f => $"required field '{f.Key}' was added"));

        if (breaking.Count > 0)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.BreakingChange, location, schema.Name, schema.Version, string.Join(", ", breaking), schema.Version + 1));
        }
        else if (added.Count > 0)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.SnapshotOutdated, location, fix, schema.Name, schema.Version, string.Join(", ", added.Select(f => f.Key)), schema.ToLine()));
        }
    }

    private static void AnalyzeCompilation(
        CompilationAnalysisContext context,
        ContractSnapshot snapshot,
        ConcurrentBag<(ContractSchema Schema, INamedTypeSymbol Type)> found)
    {
        foreach (var group in found.GroupBy(f => f.Schema.Id).Where(g => g.Count() > 1))
        {
            var names = string.Join(", ", group.Select(g => g.Type.Name).OrderBy(n => n, StringComparer.Ordinal));
            foreach (var (schema, type) in group)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.DuplicateContract, type.Locations.FirstOrDefault(), schema.Name, schema.Version, names));
            }
        }

        var ids = new HashSet<string>(found.Select(f => f.Schema.Id), StringComparer.Ordinal);
        foreach (var entry in snapshot.Entries.Values.Where(e => !e.Retired && !ids.Contains(e.Schema.Id)))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.ContractRemoved, entry.Location, entry.Schema.Name, entry.Schema.Version));
        }
    }

    private static string? ReadName(AttributeData attribute) =>
        attribute.ConstructorArguments.Length > 0 ? attribute.ConstructorArguments[0].Value as string : null;

    private static int ReadVersion(AttributeData attribute)
    {
        foreach (var argument in attribute.NamedArguments)
        {
            if (argument.Key == "Version" && argument.Value.Value is int version)
            {
                return version;
            }
        }

        return 1;
    }
}
