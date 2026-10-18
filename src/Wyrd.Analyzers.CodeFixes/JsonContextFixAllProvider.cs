using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Simplification;

namespace Wyrd.Analyzers.CodeFixes;

/// <summary>Batches attributes by declaration so several diagnostics never overwrite one another's edits.</summary>
internal sealed class JsonContextFixAllProvider() : ProjectFixAllProvider("Complete all JSON declarations")
{
    private const string Serialization = "global::System.Text.Json.Serialization";

    protected override async Task<Solution> FixProjectAsync(
        FixAllContext context, Project project, ImmutableArray<Diagnostic> diagnostics, CancellationToken cancellationToken)
    {
        while (!diagnostics.IsEmpty)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var updated = await ApplyDiagnosticsAsync(project, diagnostics, cancellationToken).ConfigureAwait(false);
            if (updated == project.Solution)
            {
                break;
            }

            project = updated.GetProject(project.Id)!;
            // Declaring missing derived types can expose new JSON name collisions. Complete those in the same action.
            var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
            if (compilation is null)
            {
                break;
            }

            var next = await compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new JsonContextAnalyzer()))
                .GetAnalyzerDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
            diagnostics = next.Where(d => context.DiagnosticIds.Contains(d.Id)
                && (context.Scope != FixAllScope.Document
                    || project.GetDocument(d.Location.SourceTree)?.Id == context.Document?.Id)).ToImmutableArray();
        }

        return project.Solution;
    }

    private static async Task<Solution> ApplyDiagnosticsAsync(
        Project project, ImmutableArray<Diagnostic> diagnostics, CancellationToken cancellationToken)
    {
        var edits = new Dictionary<DocumentId, List<AttributeEdit>>();
        foreach (var diagnostic in diagnostics.OrderBy(d => d.Properties.TryGetValue(JsonContextFix.TypeProperty, out var type) ? type : "", StringComparer.Ordinal)
            .ThenBy(d => d.Id, StringComparer.Ordinal))
        {
            if (!diagnostic.Properties.TryGetValue(JsonContextFix.TypeProperty, out var type) || type is null)
            {
                continue;
            }

            var location = diagnostic.Id == "WYRD0301" ? diagnostic.Location
                : diagnostic.AdditionalLocations.Count > 0 ? diagnostic.AdditionalLocations[0] : null;
            var document = project.GetDocument(location?.SourceTree);
            if (document is null || location is null)
            {
                continue;
            }

            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            var declaration = root?.FindNode(location.SourceSpan).FirstAncestorOrSelf<TypeDeclarationSyntax>();
            if (declaration is null)
            {
                continue;
            }

            diagnostic.Properties.TryGetValue(JsonContextFix.CaseProperty, out var caseName);
            diagnostic.Properties.TryGetValue(JsonContextFix.NameProperty, out var propertyName);
            if (propertyName is not null)
            {
                // An existing attribute can live on a different part of a partial JSON context.
                var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
                var symbol = model?.GetDeclaredSymbol(declaration, cancellationToken);
                var existing = symbol?.GetAttributes().FirstOrDefault(attribute =>
                    attribute.AttributeClass?.ToDisplayString() == "System.Text.Json.Serialization.JsonSerializableAttribute"
                    && attribute.ConstructorArguments.Length > 0
                    && attribute.ConstructorArguments[0].Value is ITypeSymbol declaredType
                    && declaredType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == type);
                if (existing?.ApplicationSyntaxReference is { } reference)
                {
                    var attribute = await reference.GetSyntaxAsync(cancellationToken).ConfigureAwait(false);
                    var owner = attribute.FirstAncestorOrSelf<TypeDeclarationSyntax>();
                    var ownerDocument = project.GetDocument(reference.SyntaxTree);
                    if (owner is not null && ownerDocument is not null)
                    {
                        declaration = owner;
                        document = ownerDocument;
                    }
                }
            }
            if (!edits.TryGetValue(document.Id, out var documentEdits))
            {
                documentEdits = [];
                edits.Add(document.Id, documentEdits);
            }

            documentEdits.Add(new AttributeEdit(declaration, type, caseName, propertyName));
        }

        var solution = project.Solution;
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in project.Documents)
        {
            var sourceRoot = await source.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            var sourceModel = await source.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            if (sourceRoot is not null && sourceModel is not null)
            {
                names.UnionWith(UsedNames(sourceRoot, sourceModel, cancellationToken));
            }
        }

        foreach (var pair in edits)
        {
            var document = project.GetDocument(pair.Key)!;
            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            if (root is null || model is null)
            {
                continue;
            }

            var groups = pair.Value.GroupBy(edit => edit.Declaration).ToDictionary(g => g.Key, g => g.ToArray());
            var updatedRoot = root.ReplaceNodes(groups.Keys, (original, rewritten) =>
            {
                foreach (var group in groups[original].GroupBy(edit => (edit.Type, edit.CaseName)))
                {
                    var edit = group.First();
                    var name = group.Select(item => item.PropertyName).FirstOrDefault(value => value is not null);
                    if (name is not null)
                    {
                        var proposed = name;
                        for (var suffix = 2; !names.Add(name); suffix++)
                        {
                            name = proposed + suffix.ToString(CultureInfo.InvariantCulture);
                        }
                    }

                    rewritten = ApplyAttribute(original, rewritten, model, edit.Type, edit.CaseName, name, cancellationToken);
                }

                return rewritten;
            });
            if (root.IsEquivalentTo(updatedRoot))
            {
                continue;
            }

            var updated = document.WithSyntaxRoot(updatedRoot);
            updated = await ImportAdder.AddImportsAsync(updated, Simplifier.Annotation, cancellationToken: cancellationToken).ConfigureAwait(false);
            updated = await Simplifier.ReduceAsync(updated, Simplifier.Annotation, cancellationToken: cancellationToken).ConfigureAwait(false);
            updated = await Formatter.FormatAsync(updated, Formatter.Annotation, cancellationToken: cancellationToken).ConfigureAwait(false);
            solution = solution.WithDocumentSyntaxRoot(document.Id, (await updated.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false))!);
        }

        return solution;
    }

    private static HashSet<string> UsedNames(SyntaxNode root, SemanticModel model, CancellationToken cancellationToken)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var declaration in root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
        {
            names.Add(declaration.Identifier.ValueText);
            if (model.GetDeclaredSymbol(declaration, cancellationToken) is not INamedTypeSymbol type)
            {
                continue;
            }

            foreach (var attribute in type.GetAttributes())
            {
                var name = attribute.NamedArguments.FirstOrDefault(argument => argument.Key == "TypeInfoPropertyName").Value.Value as string;
                if (name is not null)
                {
                    names.Add(name);
                }
            }
        }

        return names;
    }

    private static TypeDeclarationSyntax ApplyAttribute(
        TypeDeclarationSyntax original, TypeDeclarationSyntax rewritten, SemanticModel model,
        string type, string? caseName, string? name, CancellationToken cancellationToken)
    {
        var attributeClass = caseName is null ? "JsonSerializableAttribute" : "JsonDerivedTypeAttribute";
        foreach (var attribute in original.AttributeLists.SelectMany(list => list.Attributes))
        {
            if (model.GetSymbolInfo(attribute, cancellationToken).Symbol is not IMethodSymbol constructor
                || constructor.ContainingType.ToDisplayString() != "System.Text.Json.Serialization." + attributeClass
                || attribute.ArgumentList?.Arguments.FirstOrDefault()?.Expression is not TypeOfExpressionSyntax typeOf
                || model.GetTypeInfo(typeOf.Type, cancellationToken).Type?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) != type)
            {
                continue;
            }

            if (name is null)
            {
                return rewritten; // The same type may be reported by more than one diagnostic.
            }

            var argument = SyntaxFactory.AttributeArgument(SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(name)))
                .WithNameEquals(SyntaxFactory.NameEquals("TypeInfoPropertyName"));
            var arguments = attribute.ArgumentList.Arguments;
            var existing = arguments.FirstOrDefault(item => item.NameEquals?.Name.Identifier.ValueText == "TypeInfoPropertyName");
            arguments = existing is null ? arguments.Add(argument) : arguments.Replace(existing, argument);
            var updated = attribute.WithArgumentList(attribute.ArgumentList.WithArguments(arguments))
                .WithAdditionalAnnotations(Formatter.Annotation, Simplifier.Annotation);
            // Rewritten may already contain changes to another attribute or a nested union.
            var listIndex = original.AttributeLists.IndexOf((AttributeListSyntax)attribute.Parent!);
            var attributeIndex = original.AttributeLists[listIndex].Attributes.IndexOf(attribute);
            var current = rewritten.AttributeLists[listIndex].Attributes[attributeIndex];
            return rewritten.ReplaceNode(current, updated);
        }

        var text = caseName is null
            ? $"[{Serialization}.JsonSerializable(typeof({type}){(name is null ? "" : $", TypeInfoPropertyName = \"{name}\"")})]"
            : $"[{Serialization}.JsonDerivedType(typeof({type}), \"{caseName}\")]";
        var list = ((TypeDeclarationSyntax)SyntaxFactory.ParseCompilationUnit(text + " class Placeholder { }").Members[0]).AttributeLists[0]
            .WithAdditionalAnnotations(Formatter.Annotation, Simplifier.Annotation);
        return rewritten.AddAttributeLists(list);
    }

    private sealed class AttributeEdit(TypeDeclarationSyntax declaration, string type, string? caseName, string? propertyName)
    {
        public TypeDeclarationSyntax Declaration { get; } = declaration;
        public string Type { get; } = type;
        public string? CaseName { get; } = caseName;
        public string? PropertyName { get; } = propertyName;
    }
}
