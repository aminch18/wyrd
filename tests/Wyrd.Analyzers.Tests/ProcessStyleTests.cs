using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Wyrd.Analyzers.CodeFixes;

namespace Wyrd.Analyzers.Tests;

public class ProcessStyleTests
{
    private const string Domain = """
        using Wyrd;

        public sealed record OrderPlaced(string Id) : IDomainEvent;
        public sealed record PaymentConfirmed(string Id) : IDomainEvent;
        public sealed record ReserveStock(string Id) : ICommand;
        public sealed record ReleaseStock(string Id) : ICommand;
        public sealed record ShipOrder(string Id) : ICommand;
        public sealed record CancelOrder(string Id) : ICommand;

        public static class Steps
        {
            public static ReserveStock Reserve(OrderPlaced e) => new(e.Id);
            public static ReleaseStock Release(OrderPlaced e) => new(e.Id);
            public static ShipOrder Ship(OrderPlaced e) => new(e.Id);
            public static CancelOrder Cancel(OrderPlaced e) => new(e.Id);
        }
        """;

    private const string Fluent = """
        using Wyrd;
        using Wyrd.Processes;

        public static class Definitions
        {
            public static Process OrderFulfillment => Process.StartOn<OrderPlaced>(e => e.Id)
                .Then(Steps.Reserve).Retry(5.Times).Undo(Steps.Release)
                .Await<PaymentConfirmed>(e => e.Id).Within(15.Minutes)
                .Then(Steps.Ship)
                .OnCompensated(Steps.Cancel).Retry(3.Times);
        }
        """;

    private const string Pipe = """
        using Wyrd;
        using Wyrd.Processes;
        using Wyrd.Pipe;
        using static Wyrd.Pipe.Pipeline;

        public static class Definitions
        {
            public static Process OrderFulfillment => Start.On<OrderPlaced>(e => e.Id)
                >> Step(Steps.Reserve).Retry(5.Times).Undo(Steps.Release)
                >> Await<PaymentConfirmed>(e => e.Id).Within(15.Minutes)
                >> Step(Steps.Ship)
                >> OnCompensated(Steps.Cancel).Retry(3.Times);
        }
        """;

    [Theory]
    [InlineData(null)]
    [InlineData("any")]
    public async Task both_syntaxes_are_fine_unless_the_repository_picks_one(string? style)
    {
        Assert.Empty(await StyleDiagnostics(Fluent, style));
        Assert.Empty(await StyleDiagnostics(Pipe, style));
    }

    [Fact]
    public async Task a_fluent_process_is_reported_where_pipes_are_the_style()
    {
        var diagnostic = Assert.Single(await StyleDiagnostics(Fluent, "pipe"));
        Assert.Contains("uses the fluent syntax, but this repository writes processes with the pipe syntax", diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.Empty(await StyleDiagnostics(Pipe, "pipe"));
    }

    [Fact]
    public async Task a_pipe_process_is_reported_where_fluent_is_the_style()
    {
        Assert.Single(await StyleDiagnostics(Pipe, "fluent"));
        Assert.Empty(await StyleDiagnostics(Fluent, "fluent"));
    }

    [Fact]
    public async Task the_code_fix_converts_fluent_to_pipe_and_back()
    {
        var piped = await Fix(Fluent, "pipe");
        Assert.Equal(Pipe.ReplaceLineEndings("\n"), piped.ReplaceLineEndings("\n"));
        Assert.Empty(await StyleDiagnostics(piped, "pipe"));

        var back = await Fix(piped, "fluent");
        Assert.Contains(Fluent[Fluent.IndexOf("public static class", StringComparison.Ordinal)..].ReplaceLineEndings("\n"), back.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.Empty(await StyleDiagnostics(back, "fluent"));
    }

    private static async Task<ImmutableArray<Diagnostic>> StyleDiagnostics(string source, string? style)
    {
        var (compilation, _) = await Compile(source);
        return await compilation
            .WithAnalyzers([new ProcessStyleAnalyzer()], new AnalyzerOptions([], new StyleOptions(style)))
            .GetAnalyzerDiagnosticsAsync();
    }

    private static async Task<string> Fix(string source, string style)
    {
        var (compilation, document) = await Compile(source);
        var diagnostic = Assert.Single(await compilation
            .WithAnalyzers([new ProcessStyleAnalyzer()], new AnalyzerOptions([], new StyleOptions(style)))
            .GetAnalyzerDiagnosticsAsync());

        var actions = new List<CodeAction>();
        await new ProcessStyleCodeFix().RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic, (a, _) => actions.Add(a), CancellationToken.None));
        var operations = await Assert.Single(actions).GetOperationsAsync(CancellationToken.None);
        var changed = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution.GetDocument(document.Id)!;
        return (await changed.GetTextAsync()).ToString();
    }

    private static async Task<(Compilation Compilation, Document Document)> Compile(string source)
    {
        var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("Sample", LanguageNames.CSharp)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable))
            .WithParseOptions(new CSharpParseOptions(LanguageVersion.Latest))
            .AddMetadataReferences(AnalyzerHarness.References)
            .AddDocument("Domain.cs", Domain).Project;
        var document = project.AddDocument("Definitions.cs", source);
        var compilation = (await document.Project.GetCompilationAsync())!;
        var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
        return (compilation, document);
    }

    private sealed class StyleOptions(string? style) : AnalyzerConfigOptionsProvider
    {
        private readonly Options _options = new(style);

        public override AnalyzerConfigOptions GlobalOptions => _options;

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => _options;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => _options;

        private sealed class Options(string? style) : AnalyzerConfigOptions
        {
            public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value)
            {
                value = key == ProcessStyleAnalyzer.StyleOption ? style : null;
                return value is not null;
            }
        }
    }
}
