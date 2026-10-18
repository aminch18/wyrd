using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Wyrd.Analyzers;

/// <summary>
/// WYRD0013: with <c>wyrd_process_style = fluent</c> or <c>pipe</c> in .editorconfig, processes written in the other
/// syntax are reported, with a code fix that converts them. <c>any</c> (the default) allows both.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ProcessStyleAnalyzer : DiagnosticAnalyzer
{
    public const string StyleOption = "wyrd_process_style";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Diagnostics.ProcessStyle);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var process = start.Compilation.GetTypeByMetadataName("Wyrd.Processes.Process");
            var pipeStart = start.Compilation.GetTypeByMetadataName("Wyrd.Pipe.Start");
            if (process is null)
            {
                return;
            }

            start.RegisterSyntaxNodeAction(c => Analyze(c, process, pipeStart), SyntaxKind.InvocationExpression);
        });
    }

    private static void Analyze(SyntaxNodeAnalysisContext context, INamedTypeSymbol process, INamedTypeSymbol? pipeStart)
    {
        var options = context.Options.AnalyzerConfigOptionsProvider.GetOptions(context.Node.SyntaxTree);
        if (!options.TryGetValue(StyleOption, out var style) || style is not ("fluent" or "pipe"))
        {
            return;
        }

        var invocation = (InvocationExpressionSyntax)context.Node;
        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is not IMethodSymbol method)
        {
            return;
        }

        if (style == "pipe" && method.Name == "StartOn" && SymbolEqualityComparer.Default.Equals(method.ContainingType, process))
        {
            context.ReportDiagnostic(Diagnostic.Create(Diagnostics.ProcessStyle, FluentTop(invocation).GetLocation(), "fluent", "pipe"));
        }
        else if (style == "fluent" && method.Name == "On" && pipeStart is not null && SymbolEqualityComparer.Default.Equals(method.ContainingType, pipeStart))
        {
            context.ReportDiagnostic(Diagnostic.Create(Diagnostics.ProcessStyle, PipeTop(invocation).GetLocation(), "pipe", "fluent"));
        }
    }

    /// <summary>The whole fluent chain that starts at <c>Process.StartOn</c>.</summary>
    internal static ExpressionSyntax FluentTop(InvocationExpressionSyntax start)
    {
        ExpressionSyntax current = start;
        while (current.Parent is MemberAccessExpressionSyntax access && access.Expression == current
            && access.Parent is InvocationExpressionSyntax next && access.Name.Identifier.Text != "ToProcess")
        {
            current = next;
        }

        return current;
    }

    /// <summary>The whole pipe that starts at <c>Start.On</c>.</summary>
    internal static ExpressionSyntax PipeTop(InvocationExpressionSyntax start)
    {
        ExpressionSyntax current = start;
        while (current.Parent is BinaryExpressionSyntax shift && shift.IsKind(SyntaxKind.RightShiftExpression) && shift.Left == current)
        {
            current = shift;
        }

        return current;
    }
}
