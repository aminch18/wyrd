using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Wyrd.Analyzers.CodeFixes;

namespace Wyrd.Analyzers.Tests;

public class FixAllTests
{
    private const string Contracts = """
        using Wyrd;
        [IntegrationEvent("z.shipped", Version = 1)]
        public sealed record Shipped(string Id) : IIntegrationEvent;
        [IntegrationEvent("a.charged", Version = 1)]
        public sealed record Charged(string Id, string? Currency = null) : IIntegrationEvent;
        """;

    private const string Context = """
        using Wyrd;
        using System.Text.Json.Serialization;
        [WyrdJsonContext]
        public abstract partial class ShopJson : JsonSerializerContext
        {
            protected ShopJson() : base(null) { }
        }
        """;

    private const string Domain = """
        using Wyrd;
        public sealed record PlaceOrder(string Id) : ICommand;
        public sealed record OrderPlaced(string Id) : IDomainEvent;
        public enum OrderError { Rejected }
        [Union]
        public abstract record Order : IState<Order>
        {
            public static Order Initial { get; } = new None();
            public Order Evolve(IDomainEvent e) => this;
            public sealed record None : Order;
            public sealed record Placed : Order;
        }
        public sealed class PlaceOrderSlice : Slice<PlaceOrder, Order, OrderPlaced, OrderError>
        {
            public override object KeyOf(PlaceOrder c) => c.Id;
            public override Result<OrderPlaced, OrderError> Decide(PlaceOrder c, Order o) => new OrderPlaced(c.Id);
        }
        [Union]
        public abstract record Payment
        {
            public sealed record None : Payment;
            public sealed record Charged : Payment;
        }
        public sealed record ChargePayment(Payment State) : ICommand;
        """;

    [Fact]
    public async Task contract_fix_all_creates_one_complete_sorted_snapshot()
    {
        using var workspace = new AdhocWorkspace();
        var project = Project(workspace, "Shop", ("Contracts.cs", Contracts));
        var solution = await Fix(project, new ContractSnapshotCodeFix(), FixAllScope.Project);
        var snapshot = Assert.Single(solution.GetProject(project.Id)!.AdditionalDocuments);
        var text = (await snapshot.GetTextAsync()).ToString();

        Assert.StartsWith("# Integration event contracts.", text, StringComparison.Ordinal);
        Assert.EndsWith("a.charged v1 | Currency: string?; Id: string\nz.shipped v1 | Id: string\n", text, StringComparison.Ordinal);
        Assert.Empty(await Diagnostics(solution.GetProject(project.Id)!, new ContractAnalyzer()));
    }

    [Fact]
    public async Task contract_fix_all_updates_in_place_preserves_comments_and_deduplicates()
    {
        using var workspace = new AdhocWorkspace();
        var project = Project(workspace, "Shop", ("Contracts.cs", Contracts))
            .AddAdditionalDocument("contracts.wyrd.txt", "# Keep this\na.charged v1 | Id: string\n\nretired legacy.sent v1 | Id: string\na.charged v1 | Id: string\n").Project;
        var solution = await Fix(project, new ContractSnapshotCodeFix(), FixAllScope.Project);
        var snapshot = (await solution.GetProject(project.Id)!.AdditionalDocuments.Single().GetTextAsync()).ToString();

        Assert.Equal("# Keep this\na.charged v1 | Currency: string?; Id: string\n\nretired legacy.sent v1 | Id: string\nz.shipped v1 | Id: string\n", snapshot);
        Assert.Empty(await Diagnostics(solution.GetProject(project.Id)!, new ContractAnalyzer()));
    }

    [Fact]
    public async Task contract_document_scope_excludes_other_source_documents()
    {
        using var workspace = new AdhocWorkspace();
        var project = Project(workspace, "Shop", ("Contracts.cs", Contracts), ("Other.cs", """
            using Wyrd;
            [IntegrationEvent("other.sent", Version = 1)]
            public sealed record Sent(string Id) : IIntegrationEvent;
            """));
        var solution = await Fix(project, new ContractSnapshotCodeFix(), FixAllScope.Document, "Contracts.cs");
        var snapshot = (await solution.GetProject(project.Id)!.AdditionalDocuments.Single().GetTextAsync()).ToString();

        Assert.DoesNotContain("other.sent", snapshot, StringComparison.Ordinal);
        Assert.Equal("WYRD0200", Assert.Single(await Diagnostics(solution.GetProject(project.Id)!, new ContractAnalyzer())).Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task solution_scope_fixes_projects_independently(bool json)
    {
        using var workspace = new AdhocWorkspace();
        var first = Project(workspace, "First", ("First.cs", json ? Context + "\n" + Domain.Replace("using Wyrd;", "", StringComparison.Ordinal) : Contracts));
        Assert.True(workspace.TryApplyChanges(first.Solution));
        var second = Project(workspace, "Second", ("Second.cs", json ? Context + "\n" + Domain.Replace("using Wyrd;", "", StringComparison.Ordinal) : Contracts));
        CodeFixProvider provider = json ? new JsonContextCodeFix() : new ContractSnapshotCodeFix();
        var solution = await Fix(second, provider, FixAllScope.Solution);
        foreach (var project in solution.Projects)
        {
            Assert.Empty(await Diagnostics(project, json ? new JsonContextAnalyzer() : new ContractAnalyzer()));
            if (!json) { Assert.Single(project.AdditionalDocuments); }
        }
    }

    [Fact]
    public async Task json_project_fix_all_completes_types_unions_and_new_collisions_in_one_action()
    {
        using var workspace = new AdhocWorkspace();
        var project = Project(workspace, "Shop", ("Context.cs", Context), ("Domain.cs", Domain));
        var solution = await Fix(project, new JsonContextCodeFix(), FixAllScope.Project);
        var updated = solution.GetProject(project.Id)!;

        Assert.Empty(await Diagnostics(updated, new JsonContextAnalyzer()));
        var context = await Text(updated, "Context.cs");
        var domain = await Text(updated, "Domain.cs");
        Assert.Contains("TypeInfoPropertyName = \"OrderNone\"", context, StringComparison.Ordinal);
        Assert.Contains("TypeInfoPropertyName = \"PaymentNone\"", context, StringComparison.Ordinal);
        Assert.Contains("JsonDerivedType(typeof(None), \"None\")", domain, StringComparison.Ordinal);
        Assert.Equal(2, domain.Split("JsonDerivedType(typeof(None)", StringSplitOptions.None).Length - 1);
        Assert.Equal(7, context.Split("JsonSerializable(", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public async Task json_document_scope_can_update_a_context_in_another_document_without_fixing_other_sources()
    {
        using var workspace = new AdhocWorkspace();
        var project = Project(workspace, "Shop", ("Context.cs", Context), ("Domain.cs", Domain),
            ("Other.cs", "using Wyrd; public sealed record Unrelated(string Id) : ICommand;"));
        var solution = await Fix(project, new JsonContextCodeFix(), FixAllScope.Document, "Domain.cs");
        var updated = solution.GetProject(project.Id)!;
        var remaining = Assert.Single(await Diagnostics(updated, new JsonContextAnalyzer()));

        Assert.Equal("WYRD0300", remaining.Id);
        Assert.Contains("Unrelated", remaining.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.DoesNotContain("Unrelated", await Text(updated, "Context.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task json_fix_all_merges_missing_and_colliding_type_diagnostics_without_duplicate_attributes()
    {
        const string collisions = """
            using Wyrd;
            namespace Orders { public sealed record Created(string Id) : IDomainEvent; }
            namespace Payments { public sealed record Created(string Id) : IDomainEvent; }
            """;
        using var workspace = new AdhocWorkspace();
        var project = Project(workspace, "Shop", ("Context.cs", Context), ("Events.cs", collisions));
        var solution = await Fix(project, new JsonContextCodeFix(), FixAllScope.Project);
        var updated = solution.GetProject(project.Id)!;

        Assert.Empty(await Diagnostics(updated, new JsonContextAnalyzer()));
        var text = await Text(updated, "Context.cs");
        Assert.Equal(2, text.Split("JsonSerializable(", StringSplitOptions.None).Length - 1);
        Assert.Contains("TypeInfoPropertyName = \"OrdersCreated\"", text, StringComparison.Ordinal);
        Assert.Contains("TypeInfoPropertyName = \"PaymentsCreated\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task json_fix_all_updates_existing_attributes_preserving_their_other_options()
    {
        var context = Context.Replace("[WyrdJsonContext]", """
            [JsonSerializable(typeof(Orders.Created), GenerationMode = JsonSourceGenerationMode.Metadata)]
            [JsonSerializable(typeof(Payments.Created))]
            [WyrdJsonContext]
            """, StringComparison.Ordinal);
        using var workspace = new AdhocWorkspace();
        var project = Project(workspace, "Shop", ("Context.cs", context), ("Events.cs", """
            using Wyrd;
            namespace Orders { public sealed record Created(string Id) : IDomainEvent; }
            namespace Payments { public sealed record Created(string Id) : IDomainEvent; }
            """));
        var solution = await Fix(project, new JsonContextCodeFix(), FixAllScope.Project);
        var updated = solution.GetProject(project.Id)!;
        var text = await Text(updated, "Context.cs");

        Assert.Empty(await Diagnostics(updated, new JsonContextAnalyzer()));
        Assert.Equal(2, text.Split("JsonSerializable(", StringSplitOptions.None).Length - 1);
        Assert.Contains("GenerationMode = JsonSourceGenerationMode.Metadata", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task json_fix_all_updates_attributes_on_another_part_of_a_partial_context()
    {
        using var workspace = new AdhocWorkspace();
        var project = Project(workspace, "Shop", ("Context.cs", Context), ("Declarations.cs", """
            using System.Text.Json.Serialization;
            [JsonSerializable(typeof(Orders.Created))]
            [JsonSerializable(typeof(Payments.Created))]
            public abstract partial class ShopJson { }
            """), ("Events.cs", """
            using Wyrd;
            namespace Orders { public sealed record Created(string Id) : IDomainEvent; }
            namespace Payments { public sealed record Created(string Id) : IDomainEvent; }
            """));
        var solution = await Fix(project, new JsonContextCodeFix(), FixAllScope.Project);
        var updated = solution.GetProject(project.Id)!;
        Assert.Empty(await Diagnostics(updated, new JsonContextAnalyzer()));
        Assert.DoesNotContain("JsonSerializable", await Text(updated, "Context.cs"), StringComparison.Ordinal);
        var declarations = await Text(updated, "Declarations.cs");
        Assert.Equal(2, declarations.Split("JsonSerializable(", StringSplitOptions.None).Length - 1);
        Assert.Contains("TypeInfoPropertyName = \"OrdersCreated\"", declarations, StringComparison.Ordinal);
    }

    [Fact]
    public async Task json_fix_all_gives_repeated_name_suggestions_distinct_stable_names()
    {
        const string events = """
            using Wyrd;
            namespace A.Orders { public sealed record Created(string Id) : IDomainEvent; }
            namespace B.Orders { public sealed record Created(string Id) : IDomainEvent; }
            """;
        using var workspace = new AdhocWorkspace();
        var project = Project(workspace, "Shop", ("Context.cs", Context), ("Events.cs", events));
        var first = (await Fix(project, new JsonContextCodeFix(), FixAllScope.Project)).GetProject(project.Id)!;
        var second = (await Fix(project, new JsonContextCodeFix(), FixAllScope.Project)).GetProject(project.Id)!;

        Assert.Empty(await Diagnostics(first, new JsonContextAnalyzer()));
        var text = await Text(first, "Context.cs");
        Assert.Contains("TypeInfoPropertyName = \"OrdersCreated\"", text, StringComparison.Ordinal);
        Assert.Contains("TypeInfoPropertyName = \"OrdersCreated2\"", text, StringComparison.Ordinal);
        Assert.Equal(text, await Text(second, "Context.cs"));
        var unchanged = (await Fix(first, new JsonContextCodeFix(), FixAllScope.Project)).GetProject(project.Id)!;
        Assert.Equal(text, await Text(unchanged, "Context.cs"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task project_scope_leaves_other_projects_unchanged(bool json)
    {
        using var workspace = new AdhocWorkspace();
        var source = json ? Context + "\n" + Domain.Replace("using Wyrd;", "", StringComparison.Ordinal) : Contracts;
        var first = Project(workspace, "First", ("First.cs", source));
        Assert.True(workspace.TryApplyChanges(first.Solution));
        var second = Project(workspace, "Second", ("Second.cs", source));
        var solution = await Fix(second, json ? new JsonContextCodeFix() : new ContractSnapshotCodeFix(), FixAllScope.Project);
        Assert.Equal(source, await Text(solution.GetProject(first.Id)!, "First.cs"));
        Assert.Empty(solution.GetProject(first.Id)!.AdditionalDocuments);
    }

    [Fact]
    public async Task fix_all_honors_cancellation()
    {
        using var workspace = new AdhocWorkspace();
        var project = Project(workspace, "Shop", ("Contracts.cs", Contracts));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Fix(project, new ContractSnapshotCodeFix(), FixAllScope.Project,
            cancellationToken: cancellation.Token));
    }

    private static Project Project(AdhocWorkspace workspace, string name, params (string Name, string Text)[] files)
    {
        var project = workspace.AddProject(name, LanguageNames.CSharp)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable))
            .AddMetadataReferences(AnalyzerHarness.References);
        foreach (var file in files) { project = project.AddDocument(file.Name, file.Text).Project; }
        return project;
    }

    private static async Task<string> Text(Project project, string name) =>
        (await project.Documents.Single(d => d.Name == name).GetTextAsync()).ToString();

    private static async Task<ImmutableArray<Diagnostic>> Diagnostics(Project project, DiagnosticAnalyzer analyzer, CancellationToken ct = default)
    {
        var compilation = (await project.GetCompilationAsync(ct))!;
        var errors = compilation.GetDiagnostics(ct).Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors.Select(d => d.ToString())));
        var files = project.AdditionalDocuments.Select(d => (AdditionalText)new DocumentText(d)).ToImmutableArray();
        return await compilation.WithAnalyzers([analyzer], new AnalyzerOptions(files)).GetAnalyzerDiagnosticsAsync(ct);
    }

    private static async Task<Solution> Fix(Project project, CodeFixProvider provider, FixAllScope scope,
        string? documentName = null, CancellationToken cancellationToken = default)
    {
        var document = documentName is null ? project.Documents.First() : project.Documents.Single(d => d.Name == documentName);
        var context = new FixAllContext(document, provider, scope, "single-action-key", provider.FixableDiagnosticIds,
            new DiagnosticSource(provider is JsonContextCodeFix ? new JsonContextAnalyzer() : new ContractAnalyzer()), cancellationToken);
        var action = await provider.GetFixAllProvider()!.GetFixAsync(context);
        var operations = await action!.GetOperationsAsync(cancellationToken);
        return Assert.Single(operations.OfType<ApplyChangesOperation>()).ChangedSolution;
    }

    private sealed class DiagnosticSource(DiagnosticAnalyzer analyzer) : FixAllContext.DiagnosticProvider
    {
        public override async Task<IEnumerable<Diagnostic>> GetDocumentDiagnosticsAsync(Document document, CancellationToken cancellationToken) =>
            (await Diagnostics(document.Project, analyzer, cancellationToken)).Where(d => d.Location.SourceTree == document.GetSyntaxTreeAsync(cancellationToken).GetAwaiter().GetResult());

        public override async Task<IEnumerable<Diagnostic>> GetProjectDiagnosticsAsync(Project project, CancellationToken cancellationToken) =>
            (await Diagnostics(project, analyzer, cancellationToken)).Where(d => !d.Location.IsInSource);

        public override async Task<IEnumerable<Diagnostic>> GetAllDiagnosticsAsync(Project project, CancellationToken cancellationToken) =>
            await Diagnostics(project, analyzer, cancellationToken);
    }

    private sealed class DocumentText(TextDocument document) : AdditionalText
    {
        public override string Path => document.Name;
        public override SourceText GetText(CancellationToken cancellationToken = default) =>
            document.GetTextAsync(cancellationToken).GetAwaiter().GetResult();
    }
}
