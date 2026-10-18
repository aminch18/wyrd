using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Wyrd.Analyzers;

/// <summary>WYRD0020: a switch expression over a [Union] that leaves cases unhandled, naming them.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UnionSwitchAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Diagnostics.UnionCaseNotHandled);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var union = start.Compilation.GetTypeByMetadataName("Wyrd.UnionAttribute");
            if (union is null)
            {
                return;
            }

            start.RegisterOperationAction(c =>
            {
                var switchExpression = (ISwitchExpressionOperation)c.Operation;
                if (UnionCoverage.Analyze(switchExpression, union) is { Missing.Count: > 0 } coverage)
                {
                    var location = switchExpression.Syntax is SwitchExpressionSyntax syntax
                        ? syntax.SwitchKeyword.GetLocation()
                        : switchExpression.Syntax.GetLocation();
                    c.ReportDiagnostic(Diagnostic.Create(
                        Diagnostics.UnionCaseNotHandled, location, coverage.Union.Name, string.Join(", ", coverage.Missing.Select(m => m.Name))));
                }
            }, OperationKind.SwitchExpression);
        });
    }
}
