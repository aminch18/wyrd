using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Wyrd.Analyzers.Tests;

public class UnionSwitchTests
{
    private const string Payment = """
        using Wyrd;

        [Union]
        public abstract record Payment
        {
            public sealed record None : Payment;
            public sealed record Charged(decimal Amount) : Payment;
            public sealed record Refunded : Payment;
        }

        public static class Rules
        {
            public static string Describe(Payment payment) => payment switch
            {
                {0}
            };
        }
        """;

    [Fact]
    public async Task handling_every_case_needs_no_discard()
    {
        var (cs8509, wyrd) = await Analyze("Payment.None => \"none\", Payment.Charged c => $\"{c.Amount}\", Payment.Refunded => \"refunded\",");

        Assert.True(cs8509.IsSuppressed);
        Assert.Empty(wyrd);
    }

    [Fact]
    public async Task a_missing_case_is_named()
    {
        var (cs8509, wyrd) = await Analyze("Payment.None => \"none\", Payment.Charged => \"charged\",");

        Assert.False(cs8509.IsSuppressed);
        var missing = Assert.Single(wyrd);
        Assert.Equal("WYRD0020", missing.Id);
        Assert.Contains("does not handle: Refunded", missing.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public async Task or_patterns_handle_several_cases()
    {
        var (cs8509, wyrd) = await Analyze("Payment.None => \"none\", Payment.Charged or Payment.Refunded => \"paid once\",");

        Assert.True(cs8509.IsSuppressed);
        Assert.Empty(wyrd);
    }

    [Fact]
    public async Task guarded_arms_and_property_patterns_do_not_handle_a_case()
    {
        var (cs8509, wyrd) = await Analyze(
            "Payment.None => \"none\", Payment.Charged c when c.Amount > 0 => \"charged\", Payment.Charged { Amount: 0 } => \"free\", Payment.Refunded => \"refunded\",");

        Assert.False(cs8509.IsSuppressed);
        Assert.Contains("does not handle: Charged", Assert.Single(wyrd).GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public async Task a_discard_handles_everything()
    {
        var diagnostics = await All("Payment.None => \"none\", _ => \"something\",");

        Assert.DoesNotContain(diagnostics, d => d.Id is "CS8509" or "WYRD0020");
    }

    [Fact]
    public async Task switches_over_other_types_are_left_alone()
    {
        const string source = """
            public abstract record Shape
            {
                public sealed record Circle : Shape;
                public sealed record Square : Shape;
            }

            public static class Rules
            {
                public static int Sides(Shape shape) => shape switch { Shape.Circle => 0, Shape.Square => 4 };
            }
            """;

        var diagnostics = await All(source, raw: true);

        Assert.False(Assert.Single(diagnostics, d => d.Id == "CS8509").IsSuppressed);
        Assert.DoesNotContain(diagnostics, d => d.Id == "WYRD0020");
    }

    private static async Task<(Diagnostic Cs8509, Diagnostic[] Wyrd)> Analyze(string arms)
    {
        var diagnostics = await All(arms);
        return (Assert.Single(diagnostics, d => d.Id == "CS8509"), [.. diagnostics.Where(d => d.Id == "WYRD0020")]);
    }

    private static async Task<ImmutableArray<Diagnostic>> All(string armsOrSource, bool raw = false)
    {
        var source = raw ? armsOrSource : Payment.Replace("{0}", armsOrSource, StringComparison.Ordinal);
        var compilation = CSharpCompilation.Create(
            "Sample",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest))],
            AnalyzerHarness.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        // Suppressed diagnostics are hidden by default: ask for them, so tests can see what was suppressed.
        var options = new CompilationWithAnalyzersOptions(
            new AnalyzerOptions([]), onAnalyzerException: null, concurrentAnalysis: false, logAnalyzerExecutionTime: false, reportSuppressedDiagnostics: true);
        return await compilation
            .WithAnalyzers([new UnionSwitchAnalyzer(), new UnionSwitchSuppressor()], options)
            .GetAllDiagnosticsAsync();
    }
}
