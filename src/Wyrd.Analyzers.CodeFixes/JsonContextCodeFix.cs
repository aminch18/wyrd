using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Simplification;

namespace Wyrd.Analyzers.CodeFixes;

/// <summary>
/// WYRD0300: adds [JsonSerializable(typeof(T))] to the [WyrdJsonContext].
/// WYRD0301: adds [JsonDerivedType(typeof(Union.Case), "Case")] to the union.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(JsonContextCodeFix))]
[Shared]
public sealed class JsonContextCodeFix : CodeFixProvider
{
    private const string Serialization = "global::System.Text.Json.Serialization";

    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create("WYRD0300", "WYRD0301", "WYRD0302");

    public override FixAllProvider? GetFixAllProvider() => null;

    public override Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        foreach (var diagnostic in context.Diagnostics)
        {
            if (!diagnostic.Properties.TryGetValue(JsonContextFix.TypeProperty, out var type) || type is null)
            {
                continue;
            }

            if (diagnostic.Id == "WYRD0302" && diagnostic.AdditionalLocations.Count > 0
                && diagnostic.AdditionalLocations[0] is { SourceTree: { } contextTree } contextLocation
                && diagnostic.Properties.TryGetValue(JsonContextFix.NameProperty, out var unique) && unique is not null)
            {
                var document = context.Document.Project.Solution.GetDocument(contextTree);
                if (document is not null)
                {
                    context.RegisterCodeFix(
                        CodeAction.Create(
                            $"Name it '{unique}' in the JSON context",
                            ct => AddAttribute(document, contextLocation, $"[{Serialization}.JsonSerializable(typeof({type}), TypeInfoPropertyName = \"{unique}\")]", ct),
                            equivalenceKey: $"{nameof(JsonContextCodeFix)}:name:{type}"),
                        diagnostic);
                }
            }
            else if (diagnostic.Id == "WYRD0300" && diagnostic.AdditionalLocations.Count > 0
                && diagnostic.AdditionalLocations[0] is { SourceTree: { } tree } target)
            {
                var document = context.Document.Project.Solution.GetDocument(tree);
                if (document is not null)
                {
                    var parts = type.Split('.');
                    var name = parts[parts.Length - 1];
                    context.RegisterCodeFix(
                        CodeAction.Create(
                            $"Declare '{name}' in the JSON context",
                            ct => AddAttribute(document, target, $"[{Serialization}.JsonSerializable(typeof({type}))]", ct),
                            equivalenceKey: $"{nameof(JsonContextCodeFix)}:{type}"),
                        diagnostic);
                }
            }
            else if (diagnostic.Id == "WYRD0301" && diagnostic.Properties.TryGetValue(JsonContextFix.CaseProperty, out var @case) && @case is not null)
            {
                context.RegisterCodeFix(
                    CodeAction.Create(
                        $"Declare '{@case}' as a JSON derived type",
                        ct => AddAttribute(context.Document, diagnostic.Location, $"[{Serialization}.JsonDerivedType(typeof({type}), \"{@case}\")]", ct),
                        equivalenceKey: $"{nameof(JsonContextCodeFix)}:{type}"),
                    diagnostic);
            }
        }

        return Task.CompletedTask;
    }

    private static async Task<Document> AddAttribute(Document document, Location location, string attribute, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var declaration = root?.FindNode(location.SourceSpan).FirstAncestorOrSelf<TypeDeclarationSyntax>();
        if (root is null || declaration is null)
        {
            return document;
        }

        var list = ((TypeDeclarationSyntax)SyntaxFactory.ParseCompilationUnit(attribute + " class Placeholder { }").Members[0]).AttributeLists[0]
            .WithAdditionalAnnotations(Formatter.Annotation, Simplifier.Annotation);
        var updated = document.WithSyntaxRoot(root.ReplaceNode(declaration, declaration.AddAttributeLists(list)));

        updated = await ImportAdder.AddImportsAsync(updated, Simplifier.Annotation, cancellationToken: cancellationToken).ConfigureAwait(false);
        updated = await Simplifier.ReduceAsync(updated, Simplifier.Annotation, cancellationToken: cancellationToken).ConfigureAwait(false);
        return await Formatter.FormatAsync(updated, Formatter.Annotation, cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
