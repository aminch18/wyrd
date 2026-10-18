using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Wyrd.Analyzers;

/// <summary>
/// The compiler can't know a [Union] is closed, so a switch handling every case still gets CS8509 and people add
/// '_' — which then swallows every case added later. Wyrd knows the cases: it suppresses CS8509 exactly when all
/// of them are handled.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UnionSwitchSuppressor : DiagnosticSuppressor
{
    public override ImmutableArray<SuppressionDescriptor> SupportedSuppressions { get; } =
        ImmutableArray.Create(Diagnostics.ExhaustiveUnionSwitch);

    public override void ReportSuppressions(SuppressionAnalysisContext context)
    {
        var union = context.Compilation.GetTypeByMetadataName("Wyrd.UnionAttribute");
        if (union is null)
        {
            return;
        }

        foreach (var diagnostic in context.ReportedDiagnostics)
        {
            var tree = diagnostic.Location.SourceTree;
            if (tree is null)
            {
                continue;
            }

            var node = tree.GetRoot(context.CancellationToken).FindNode(diagnostic.Location.SourceSpan);
            var syntax = node.AncestorsAndSelf().OfType<SwitchExpressionSyntax>().FirstOrDefault();
            if (syntax is null)
            {
                continue;
            }

            var model = context.GetSemanticModel(tree);
            if (model.GetOperation(syntax, context.CancellationToken) is ISwitchExpressionOperation operation
                && UnionCoverage.Analyze(operation, union) is { Missing.Count: 0 })
            {
                context.ReportSuppression(Suppression.Create(Diagnostics.ExhaustiveUnionSwitch, diagnostic));
            }
        }
    }
}
