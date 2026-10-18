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

namespace Wyrd.Analyzers.CodeFixes;

/// <summary>WYRD0013: rewrites a process from fluent to pipe syntax, or back, keeping every argument as written.</summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ProcessStyleCodeFix))]
[Shared]
public sealed class ProcessStyleCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create("WYRD0013");

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root?.FindNode(context.Span, getInnermostNodeForTie: false) is not ExpressionSyntax top)
        {
            return;
        }

        var toPipe = ProcessChain.FromFluent(top) is not null;
        context.RegisterCodeFix(
            CodeAction.Create(
                toPipe ? "Write this process with the pipe syntax" : "Write this process with the fluent syntax",
                ct => Convert(context.Document, top, toPipe, ct),
                equivalenceKey: toPipe ? "WYRD0013:pipe" : "WYRD0013:fluent"),
            context.Diagnostics);
    }

    private static async Task<Document> Convert(Document document, ExpressionSyntax top, bool toPipe, CancellationToken cancellationToken)
    {
        var chain = toPipe ? ProcessChain.FromFluent(top) : ProcessChain.FromPipe(top);
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (chain is null || root is not CompilationUnitSyntax unit)
        {
            return document;
        }

        var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        var line = text.Lines.GetLineFromPosition(top.SpanStart).ToString();
        var indent = line.Substring(0, line.Length - line.TrimStart().Length) + "    ";
        var rewritten = SyntaxFactory.ParseExpression(toPipe ? chain.ToPipe(indent) : chain.ToFluent(indent)).WithTriviaFrom(top);

        unit = unit.ReplaceNode(top, rewritten);
        unit = toPipe
            ? AddUsing(AddUsing(unit, "Wyrd.Pipe", isStatic: false), "Wyrd.Pipe.Pipeline", isStatic: true)
            : AddUsing(unit, "Wyrd.Processes", isStatic: false);
        return document.WithSyntaxRoot(unit);
    }

    private static CompilationUnitSyntax AddUsing(CompilationUnitSyntax unit, string name, bool isStatic)
    {
        if (unit.Usings.Any(u => u.Name?.ToString() == name && u.StaticKeyword.IsKind(SyntaxKind.StaticKeyword) == isStatic))
        {
            return unit;
        }

        var directive = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(name)).NormalizeWhitespace().WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed);
        if (isStatic)
        {
            directive = directive.WithStaticKeyword(SyntaxFactory.Token(SyntaxKind.StaticKeyword).WithTrailingTrivia(SyntaxFactory.Space));
        }

        return unit.AddUsings(directive);
    }
}
