using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;

namespace Wyrd.Analyzers.CodeFixes;

/// <summary>Collects a scope's diagnostics before applying one coordinated change per project.</summary>
internal abstract class ProjectFixAllProvider(string title) : FixAllProvider
{
    public override Task<CodeAction?> GetFixAsync(FixAllContext fixAllContext) =>
        Task.FromResult<CodeAction?>(CodeAction.Create(title,
            ct => FixSolutionAsync(fixAllContext, ct), fixAllContext.CodeActionEquivalenceKey));

    private async Task<Solution> FixSolutionAsync(FixAllContext context, CancellationToken cancellationToken)
    {
        var solution = context.Solution;
        var projects = context.Scope == FixAllScope.Solution
            ? context.Solution.Projects.Where(p => p.Language == LanguageNames.CSharp)
            : new[] { context.Project };
        foreach (var original in projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var diagnostics = context.Scope == FixAllScope.Document && context.Document is { } document
                ? await context.GetDocumentDiagnosticsAsync(document).ConfigureAwait(false)
                : await context.GetAllDiagnosticsAsync(original).ConfigureAwait(false);
            if (!diagnostics.IsEmpty)
            {
                solution = await FixProjectAsync(context, solution.GetProject(original.Id)!, diagnostics, cancellationToken).ConfigureAwait(false);
            }
        }

        return solution;
    }

    protected abstract Task<Solution> FixProjectAsync(
        FixAllContext context, Project project, ImmutableArray<Diagnostic> diagnostics, CancellationToken cancellationToken);
}
