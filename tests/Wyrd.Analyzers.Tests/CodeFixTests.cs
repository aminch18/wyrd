using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Wyrd.Analyzers.CodeFixes;

namespace Wyrd.Analyzers.Tests;

public class CodeFixTests
{
    private const string ChargedV1 = """
        using Wyrd;

        [IntegrationEvent("payments.payment-charged", Version = 1)]
        public sealed record PaymentChargedV1(string OrderId, decimal Amount, string? Currency = null) : IIntegrationEvent;
        """;

    [Fact]
    public async Task writes_a_new_contract_into_an_existing_snapshot()
    {
        var snapshot = await ApplyFix(ChargedV1, "# Contracts\nshipping.order-shipped v1 | OrderId: string\n");

        Assert.Equal(
            "# Contracts\nshipping.order-shipped v1 | OrderId: string\npayments.payment-charged v1 | Amount: decimal; Currency: string?; OrderId: string\n",
            snapshot);
    }

    [Fact]
    public async Task replaces_an_outdated_line_in_place()
    {
        var snapshot = await ApplyFix(
            ChargedV1,
            "payments.payment-charged v1 | Amount: decimal; OrderId: string\nshipping.order-shipped v1 | OrderId: string\n");

        Assert.Equal(
            "payments.payment-charged v1 | Amount: decimal; Currency: string?; OrderId: string\nshipping.order-shipped v1 | OrderId: string\n",
            snapshot);
    }

    [Fact]
    public async Task creates_the_snapshot_when_there_is_none()
    {
        var snapshot = await ApplyFix(ChargedV1, existingSnapshot: null);

        Assert.EndsWith("payments.payment-charged v1 | Amount: decimal; Currency: string?; OrderId: string\n", snapshot, StringComparison.Ordinal);
        Assert.StartsWith("#", snapshot, StringComparison.Ordinal);
    }

    [Fact]
    public async Task the_fixed_snapshot_makes_the_build_clean()
    {
        var snapshot = await ApplyFix(ChargedV1, existingSnapshot: "");

        Assert.Empty(await AnalyzerHarness.Analyze(ChargedV1.Replace("using Wyrd;", "", StringComparison.Ordinal), snapshot));
    }

    private static async Task<string> ApplyFix(string source, string? existingSnapshot)
    {
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("Sample", LanguageNames.CSharp)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable))
            .AddMetadataReferences(AnalyzerHarness.References);
        var document = project.AddDocument("Contracts.cs", source);
        project = document.Project;
        if (existingSnapshot is not null)
        {
            project = project.AddAdditionalDocument("contracts.wyrd.txt", existingSnapshot).Project;
            document = project.GetDocument(document.Id)!;
        }

        var compilation = (await project.GetCompilationAsync())!;
        var diagnostics = await compilation
            .WithAnalyzers([new ContractAnalyzer()], new AnalyzerOptions([.. project.AdditionalDocuments.Select(d => (AdditionalText)new DocumentText(d))]))
            .GetAnalyzerDiagnosticsAsync();
        var diagnostic = Assert.Single(diagnostics, d => d.Id is "WYRD0200" or "WYRD0202");

        var actions = new List<CodeAction>();
        var context = new CodeFixContext(document, diagnostic, (action, _) => actions.Add(action), CancellationToken.None);
        await new ContractSnapshotCodeFix().RegisterCodeFixesAsync(context);

        var operations = await Assert.Single(actions).GetOperationsAsync(CancellationToken.None);
        var solution = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution;
        var snapshot = solution.GetProject(project.Id)!.AdditionalDocuments.Single(d => d.Name == "contracts.wyrd.txt");
        return (await snapshot.GetTextAsync()).ToString();
    }

    private sealed class DocumentText(TextDocument document) : AdditionalText
    {
        public override string Path => document.Name;

        public override SourceText GetText(CancellationToken cancellationToken = default) =>
            document.GetTextAsync(cancellationToken).GetAwaiter().GetResult();
    }
}
