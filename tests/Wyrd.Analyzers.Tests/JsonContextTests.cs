using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Wyrd.Analyzers.CodeFixes;

namespace Wyrd.Analyzers.Tests;

public class JsonContextTests
{
    // Without the System.Text.Json generator in these tests, the context is abstract: same attributes, no generated code.
    private const string Context = """
        using System.Text.Json.Serialization;
        using Wyrd;

        namespace Shop;

        [WyrdJsonContext]
        [JsonSerializable(typeof(ChargePayment))]
        public abstract partial class ShopJson : JsonSerializerContext
        {
            protected ShopJson() : base(null) { }
        }
        """;

    private const string Payments = """
        using System.Text.Json.Serialization;
        using Wyrd;

        namespace Shop;

        public sealed record ChargePayment(string OrderId) : ICommand;
        public sealed record PaymentCharged(string OrderId) : IDomainEvent;
        public enum PaymentError { Declined }

        [Union]
        [JsonDerivedType(typeof(Payment.None), "None")]
        public abstract record Payment : IState<Payment>
        {
            public static Payment Initial { get; } = new None();
            public Payment Evolve(IDomainEvent e) => e is PaymentCharged ? new Charged() : this;
            public sealed record None : Payment;
            public sealed record Charged : Payment;
        }

        public sealed class ChargePaymentSlice : Slice<ChargePayment, Payment, PaymentCharged, PaymentError>
        {
            public override object KeyOf(ChargePayment c) => c.OrderId;
            public override Result<PaymentCharged, PaymentError> Decide(ChargePayment c, Payment p) => new PaymentCharged(c.OrderId);
        }
        """;

    [Fact]
    public async Task without_a_context_types_are_not_required()
    {
        var diagnostics = await Analyze(("Payments.cs", Payments));

        Assert.DoesNotContain(diagnostics, d => d.Id == "WYRD0300");
    }

    [Fact]
    public async Task without_a_context_an_unannotated_union_is_fine()
    {
        var diagnostics = await Analyze(("Payments.cs", Payments.Replace("[JsonDerivedType(typeof(Payment.None), \"None\")]", "", StringComparison.Ordinal)));

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task a_partially_declared_union_is_an_error_even_without_a_context()
    {
        var diagnostics = await Analyze(("Payments.cs", Payments));

        Assert.Equal("WYRD0301", Assert.Single(diagnostics).Id);
    }

    [Fact]
    public async Task every_serialized_type_must_be_declared()
    {
        var diagnostics = await Analyze(("ShopJson.cs", Context), ("Payments.cs", Payments));

        Assert.Equal(
            ["Payment", "PaymentCharged", "PaymentError"],
            diagnostics.Where(d => d.Id == "WYRD0300").Select(d => d.GetMessage(CultureInfo.InvariantCulture).Split('\'')[1]).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task every_union_case_must_be_a_derived_type()
    {
        var diagnostics = await Analyze(("ShopJson.cs", Context), ("Payments.cs", Payments));

        var missing = Assert.Single(diagnostics, d => d.Id == "WYRD0301");
        Assert.Contains("'Charged' is not declared", missing.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public async Task the_code_fixes_complete_the_context_and_the_union()
    {
        var project = Project(("ShopJson.cs", Context), ("Payments.cs", Payments));

        for (var i = 0; i < 10 && await FirstFixable(project) is { } diagnostic; i++)
        {
            project = await ApplyFix(project, diagnostic);
        }

        Assert.Empty(await Diagnostics(project));
        var context = await Text(project, "ShopJson.cs");
        Assert.Contains("[JsonSerializable(typeof(PaymentError))]", context, StringComparison.Ordinal);
        var payments = await Text(project, "Payments.cs");
        Assert.True(payments.Contains("[JsonDerivedType(typeof(Charged), \"Charged\")]", StringComparison.Ordinal), payments);
    }

    private const string TwoUnions = """
        using System.Text.Json.Serialization;
        using Wyrd;

        namespace Shop;

        [WyrdJsonContext]
        [JsonSerializable(typeof(Order))]
        [JsonSerializable(typeof(Payment))]
        public abstract partial class ShopJson : JsonSerializerContext
        {
            protected ShopJson() : base(null) { }
        }

        [Union]
        [JsonDerivedType(typeof(None), "None")]
        [JsonDerivedType(typeof(Placed), "Placed")]
        public abstract record Order
        {
            public sealed record None : Order;
            public sealed record Placed : Order;
        }

        [Union]
        [JsonDerivedType(typeof(None), "None")]
        [JsonDerivedType(typeof(Charged), "Charged")]
        public abstract record Payment
        {
            public sealed record None : Payment;
            public sealed record Charged : Payment;
        }
        """;

    [Fact]
    public async Task union_cases_with_the_same_name_collide()
    {
        var collisions = (await Analyze(("Shop.cs", TwoUnions))).Where(d => d.Id == "WYRD0302").ToList();

        Assert.Equal(2, collisions.Count);
        Assert.Contains(
            "'Shop.Order.None' and 'Shop.Payment.None' share the name 'None' in ShopJson",
            collisions[0].GetMessage(CultureInfo.InvariantCulture),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task the_code_fix_gives_a_colliding_type_a_unique_name()
    {
        var project = Project(("Shop.cs", TwoUnions));

        for (var i = 0; i < 10 && await FirstFixable(project) is { } diagnostic; i++)
        {
            project = await ApplyFix(project, diagnostic);
        }

        Assert.Empty(await Diagnostics(project));
        Assert.Contains("TypeInfoPropertyName = \"OrderNone\")]", await Text(project, "Shop.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task an_explicit_name_resolves_the_collision() =>
        Assert.DoesNotContain(
            await Analyze(("Shop.cs", TwoUnions.Replace(
                "[JsonSerializable(typeof(Payment))]",
                "[JsonSerializable(typeof(Payment))]\n[JsonSerializable(typeof(Payment.None), TypeInfoPropertyName = \"PaymentNone\")]",
                StringComparison.Ordinal))),
            d => d.Id == "WYRD0302");

    [Fact]
    public async Task types_with_the_same_name_in_different_namespaces_collide()
    {
        const string source = """
            using System.Text.Json.Serialization;
            using Wyrd;

            namespace Shop
            {
                [WyrdJsonContext]
                [JsonSerializable(typeof(Orders.Created))]
                [JsonSerializable(typeof(Payments.Created))]
                public abstract partial class ShopJson : JsonSerializerContext
                {
                    protected ShopJson() : base(null) { }
                }
            }

            namespace Shop.Orders { public sealed record Created(string Id) : IDomainEvent; }
            namespace Shop.Payments { public sealed record Created(string Id) : IDomainEvent; }
            """;

        var collisions = (await Analyze(("Shop.cs", source))).Where(d => d.Id == "WYRD0302").ToList();

        Assert.Equal(2, collisions.Count);
        Assert.Contains("TypeInfoPropertyName = \"OrdersCreated\"", collisions[0].GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public async Task types_used_inside_messages_are_checked_too()
    {
        const string source = """
            using System.Text.Json.Serialization;
            using Wyrd;

            namespace Shop
            {
                [WyrdJsonContext]
                [JsonSerializable(typeof(Shipping.ShipOrder))]
                [JsonSerializable(typeof(Billing.SendInvoice))]
                public abstract partial class ShopJson : JsonSerializerContext
                {
                    protected ShopJson() : base(null) { }
                }
            }

            namespace Shop.Shipping
            {
                public sealed record Address(string Street);
                public sealed record ShipOrder(string OrderId, Address To) : ICommand;
            }

            namespace Shop.Billing
            {
                public sealed record Address(string Street, string TaxId);
                public sealed record SendInvoice(string OrderId, Address To) : ICommand;
            }
            """;

        var collisions = (await Analyze(("Shop.cs", source))).Where(d => d.Id == "WYRD0302").ToList();

        Assert.Equal(2, collisions.Count);
    }

    private static async Task<ImmutableArray<Diagnostic>> Analyze(params (string Name, string Source)[] files) =>
        await Diagnostics(Project(files));

    private static Project Project(params (string Name, string Source)[] files)
    {
        var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("Sample", LanguageNames.CSharp)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable))
            .AddMetadataReferences(AnalyzerHarness.References);
        foreach (var (name, source) in files)
        {
            project = project.AddDocument(name, source).Project;
        }

        return project;
    }

    private static async Task<ImmutableArray<Diagnostic>> Diagnostics(Project project)
    {
        var compilation = (await project.GetCompilationAsync())!;
        var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
        return await compilation.WithAnalyzers([new JsonContextAnalyzer()], new AnalyzerOptions([])).GetAnalyzerDiagnosticsAsync();
    }

    private static async Task<Diagnostic?> FirstFixable(Project project) =>
        (await Diagnostics(project)).OrderBy(d => d.Id, StringComparer.Ordinal).FirstOrDefault();

    private static async Task<Project> ApplyFix(Project project, Diagnostic diagnostic)
    {
        var document = project.GetDocument(diagnostic.Location.SourceTree)!;
        var actions = new List<CodeAction>();
        await new JsonContextCodeFix().RegisterCodeFixesAsync(
            new CodeFixContext(document, diagnostic, (action, _) => actions.Add(action), CancellationToken.None));
        var operations = await Assert.Single(actions).GetOperationsAsync(CancellationToken.None);
        return operations.OfType<ApplyChangesOperation>().Single().ChangedSolution.GetProject(project.Id)!;
    }

    private static async Task<string> Text(Project project, string name) =>
        (await project.Documents.Single(d => d.Name == name).GetTextAsync()).ToString();
}
