using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Wyrd.Analyzers.Tests;

/// <summary>Compiles a snippet against Wyrd, with an optional contracts.wyrd.txt, and runs the analyzer.</summary>
internal static class AnalyzerHarness
{
    internal static readonly MetadataReference[] References =
    [
        .. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path)),
        MetadataReference.CreateFromFile(typeof(IIntegrationEvent).Assembly.Location),
    ];

    public static async Task<ImmutableArray<Diagnostic>> Analyze(string source, string? contracts = null)
    {
        var compilation = CSharpCompilation.Create(
            "Sample",
            [CSharpSyntaxTree.ParseText("using Wyrd;\n" + source, new CSharpParseOptions(LanguageVersion.Latest))],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var compileErrors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(compileErrors.Count == 0, string.Join(Environment.NewLine, compileErrors));

        ImmutableArray<AdditionalText> files = contracts is null ? [] : [new InMemoryText("contracts.wyrd.txt", contracts)];
        var diagnostics = await compilation
            .WithAnalyzers([new ContractAnalyzer(), new SliceAnalyzer(), new NamingAnalyzer(), new ProcessAnalyzer()], new AnalyzerOptions(files))
            .GetAnalyzerDiagnosticsAsync();

        return [.. diagnostics.OrderBy(d => d.Id, StringComparer.Ordinal)];
    }

    private sealed class InMemoryText(string path, string text) : AdditionalText
    {
        public override string Path => path;

        public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(text);
    }
}
