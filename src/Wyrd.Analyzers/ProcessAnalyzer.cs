using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Wyrd.Analyzers;

/// <summary>
/// Checks that process definitions, in fluent and pipe syntax, say what Wyrd won't assume: how each step retries
/// (WYRD0014, no default), how each step is undone (WYRD0011), and how long a wait lasts (WYRD0012).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ProcessAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Diagnostics.RetryUnsaid, Diagnostics.CompensationUnsaid, Diagnostics.AwaitWithoutTimeout);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var builder = start.Compilation.GetTypeByMetadataName("Wyrd.Processes.ProcessBuilder`1");
            var pipeline = start.Compilation.GetTypeByMetadataName("Wyrd.Pipe.Pipeline");
            if (builder is null)
            {
                return;
            }

            start.RegisterSyntaxNodeAction(c => Analyze(c, builder, pipeline), SyntaxKind.InvocationExpression);
        });
    }

    private static void Analyze(SyntaxNodeAnalysisContext context, INamedTypeSymbol builder, INamedTypeSymbol? pipeline)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is not IMethodSymbol method)
        {
            return;
        }

        var fluent = SymbolEqualityComparer.Default.Equals(method.ContainingType.OriginalDefinition, builder);
        var piped = pipeline is not null && SymbolEqualityComparer.Default.Equals(method.ContainingType, pipeline);
        if ((!fluent && !piped) || method.Name is not ("Then" or "Step" or "Await"))
        {
            return;
        }

        if (fluent && method.Name == "Step")
        {
            return;
        }

        var (modifiers, top, laterStep) = Modifiers(invocation);
        var location = invocation.GetLocation();

        if (method.Name == "Await")
        {
            if (!modifiers.Contains("Within"))
            {
                var awaited = method.TypeArguments.FirstOrDefault()?.Name ?? "?";
                context.ReportDiagnostic(Diagnostic.Create(Diagnostics.AwaitWithoutTimeout, location, awaited));
            }

            return;
        }

        var step = invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression.ToString() ?? method.Name;
        var undone = modifiers.Contains("Undo");

        if (!modifiers.Contains("Retry") && !modifiers.Contains("NoRetry"))
        {
            context.ReportDiagnostic(Diagnostic.Create(Diagnostics.RetryUnsaid, location, step));
        }

        // The last step needs no undo: nothing after it can fail and trigger compensation.
        var isLast = fluent ? !laterStep : !LaterStageInPipe(top);
        if (!undone && !modifiers.Contains("NoCompensation") && !isLast)
        {
            context.ReportDiagnostic(Diagnostic.Create(Diagnostics.CompensationUnsaid, location, step));
        }
    }

    /// <summary>
    /// Walks the calls chained after a step (<c>.Retry(...).Undo(...)</c>) until the next step. Returns the modifier
    /// names, the outermost expression of the chain, and whether another step or wait follows in the same chain.
    /// </summary>
    private static (HashSet<string> Modifiers, ExpressionSyntax Top, bool LaterStep) Modifiers(InvocationExpressionSyntax start)
    {
        var modifiers = new HashSet<string>();
        ExpressionSyntax current = start;
        while (current.Parent is MemberAccessExpressionSyntax access && access.Expression == current
            && access.Parent is InvocationExpressionSyntax next)
        {
            var name = access.Name.Identifier.Text;
            if (name is "Then" or "Await")
            {
                return (modifiers, current, true);
            }

            if (name is "OnCompensated" or "ToProcess")
            {
                break;
            }

            modifiers.Add(name);
            current = next;
        }

        return (modifiers, current, false);
    }

    /// <summary>In <c>a &gt;&gt; b &gt;&gt; c</c>, whether a step or wait comes after the stage ending at <paramref name="stage"/>.</summary>
    private static bool LaterStageInPipe(ExpressionSyntax stage)
    {
        SyntaxNode node = stage;
        while (node.Parent is BinaryExpressionSyntax shift && shift.IsKind(SyntaxKind.RightShiftExpression))
        {
            if (shift.Left == node && StageName(shift.Right) is "Step" or "Await")
            {
                return true;
            }

            node = shift;
        }

        return false;
    }

    /// <summary>The name of the call a pipe stage starts with: <c>Step</c> in <c>Step(x).Undo(y)</c>.</summary>
    private static string? StageName(ExpressionSyntax stage)
    {
        var current = stage;
        while (current is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax access }
            && access.Expression is InvocationExpressionSyntax inner)
        {
            current = inner;
        }

        return current is InvocationExpressionSyntax invocation
            ? invocation.Expression switch
            {
                IdentifierNameSyntax identifier => identifier.Identifier.Text,
                GenericNameSyntax generic => generic.Identifier.Text,
                MemberAccessExpressionSyntax access => access.Name.Identifier.Text,
                _ => null,
            }
            : null;
    }
}
