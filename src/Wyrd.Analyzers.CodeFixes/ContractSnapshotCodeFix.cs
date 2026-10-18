using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Text;

namespace Wyrd.Analyzers.CodeFixes;

/// <summary>Writes the suggested contract line into contracts.wyrd.txt (WYRD0200, WYRD0202).</summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ContractSnapshotCodeFix))]
[Shared]
public sealed class ContractSnapshotCodeFix : CodeFixProvider
{
    private const string FileName = "contracts.wyrd.txt";

    private const string Header =
        "# Integration event contracts. Reviewed in pull requests; breaking a line is a build error.\n\n";

    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create("WYRD0200", "WYRD0202");

    public override FixAllProvider GetFixAllProvider() => new SnapshotFixAllProvider();

    public override Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        foreach (var diagnostic in context.Diagnostics)
        {
            if (!diagnostic.Properties.TryGetValue(SnapshotFix.LineProperty, out var line) || line is null
                || !diagnostic.Properties.TryGetValue(SnapshotFix.IdProperty, out var id) || id is null)
            {
                continue;
            }

            var project = context.Document.Project;
            context.RegisterCodeFix(
                CodeAction.Create(
                    $"Write '{id}' to {FileName}",
                    cancellationToken => WriteLine(project, id, line, cancellationToken),
                    equivalenceKey: $"{nameof(ContractSnapshotCodeFix)}:{id}"),
                diagnostic);
        }

        return Task.CompletedTask;
    }

    internal static async Task<Solution> WriteLine(Project project, string id, string line, CancellationToken cancellationToken)
    {
        var snapshot = project.AdditionalDocuments.FirstOrDefault(d =>
            string.Equals(d.Name, FileName, StringComparison.OrdinalIgnoreCase));

        if (snapshot is null)
        {
            var directory = project.FilePath is null ? null : System.IO.Path.GetDirectoryName(project.FilePath);
            var path = directory is null ? null : System.IO.Path.Combine(directory, FileName);
            return project.AddAdditionalDocument(FileName, SourceText.From(Header + line + "\n"), filePath: path).Project.Solution;
        }

        var text = await snapshot.GetTextAsync(cancellationToken).ConfigureAwait(false);
        var lines = text.Lines.Select(l => l.ToString()).ToList();
        if (lines.Count > 0 && lines[lines.Count - 1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        var existing = lines.FindIndex(l => IsLineFor(l, id));
        if (existing >= 0)
        {
            lines[existing] = line;
        }
        else
        {
            lines.Add(line);
        }

        var updated = string.Join("\n", lines) + "\n";
        return project.Solution.WithAdditionalDocumentText(snapshot.Id, SourceText.From(updated, text.Encoding));
    }

    private sealed class SnapshotFixAllProvider() : ProjectFixAllProvider("Write all contracts to contracts.wyrd.txt")
    {
        protected override async Task<Solution> FixProjectAsync(
            FixAllContext context, Project project, ImmutableArray<Diagnostic> diagnostics, CancellationToken cancellationToken)
        {
            var replacements = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var diagnostic in diagnostics)
            {
                if (diagnostic.Properties.TryGetValue(SnapshotFix.IdProperty, out var id) && id is not null
                    && diagnostic.Properties.TryGetValue(SnapshotFix.LineProperty, out var line) && line is not null)
                {
                    // Conflicting declarations are diagnosed separately. Pick deterministically here.
                    if (!replacements.TryGetValue(id, out var previous) || string.CompareOrdinal(line, previous) < 0)
                    {
                        replacements[id] = line;
                    }
                }
            }

            if (replacements.Count == 0)
            {
                return project.Solution;
            }

            var snapshot = project.AdditionalDocuments.FirstOrDefault(d =>
                string.Equals(d.Name, FileName, StringComparison.OrdinalIgnoreCase));
            var text = snapshot is null ? SourceText.From(Header)
                : await snapshot.GetTextAsync(cancellationToken).ConfigureAwait(false);
            var lines = text.Lines.Select(l => l.ToString()).ToList();
            if (lines.Count > 0 && lines[lines.Count - 1].Length == 0)
            {
                lines.RemoveAt(lines.Count - 1);
            }

            foreach (var replacement in replacements)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var existing = lines.FindIndex(line => IsLineFor(line, replacement.Key));
                if (existing >= 0)
                {
                    lines[existing] = replacement.Value;
                    for (var index = lines.Count - 1; index > existing; index--)
                    {
                        if (IsLineFor(lines[index], replacement.Key))
                        {
                            lines.RemoveAt(index);
                        }
                    }
                }
                else
                {
                    lines.Add(replacement.Value);
                }
            }

            var updated = SourceText.From(string.Join("\n", lines) + "\n", text.Encoding);
            if (snapshot is not null)
            {
                return project.Solution.WithAdditionalDocumentText(snapshot.Id, updated);
            }

            var directory = project.FilePath is null ? null : System.IO.Path.GetDirectoryName(project.FilePath);
            var path = directory is null ? null : System.IO.Path.Combine(directory, FileName);
            return project.AddAdditionalDocument(FileName, updated, filePath: path).Project.Solution;
        }
    }

    private static bool IsLineFor(string line, string id)
    {
        var content = line.Trim();
        return content.StartsWith(id + " ", StringComparison.Ordinal) || string.Equals(content, id, StringComparison.Ordinal);
    }
}
