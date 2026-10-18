namespace Wyrd.Analyzers.Tests;

public class OwnershipAndNamingTests
{
    private const string Domain = """
        public sealed record ChargePayment(string OrderId) : ICommand;
        public sealed record PaymentCharged(string OrderId) : IDomainEvent;
        public sealed record Payment : IState<Payment>
        {
            public static Payment Initial { get; } = new();
            public Payment Evolve(IDomainEvent domainEvent) => this;
        }
        """;

    private const string Slice = """
        public sealed class {0} : Slice<ChargePayment, Payment, PaymentCharged, string>
        {
            public override object KeyOf(ChargePayment command) => command.OrderId;
            public override Result<PaymentCharged, string> Decide(ChargePayment command, Payment state) => new PaymentCharged(command.OrderId);
        }
        """;

    [Fact]
    public async Task one_slice_per_command_is_clean() =>
        Assert.Empty(await AnalyzerHarness.Analyze(Domain + Slice.Replace("{0}", "ChargePaymentSlice", StringComparison.Ordinal)));

    [Fact]
    public async Task two_slices_for_the_same_command_are_an_error()
    {
        var source = Domain
            + Slice.Replace("{0}", "ChargePaymentSlice", StringComparison.Ordinal)
            + Slice.Replace("{0}", "AnotherChargeSlice", StringComparison.Ordinal);

        var diagnostics = await AnalyzerHarness.Analyze(source);

        Assert.Equal(2, diagnostics.Count(d => d.Id == "WYRD0002"));
        Assert.Contains(
            "decided by more than one slice (AnotherChargeSlice, ChargePaymentSlice)",
            diagnostics[0].GetMessage(CultureInfo.InvariantCulture),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("PaymentCharged")]
    [InlineData("OrderPaid")]
    [InlineData("InvoiceSent")]
    public async Task events_in_the_past_tense_are_clean(string name) =>
        Assert.Empty(await AnalyzerHarness.Analyze($"public sealed record {name}(string Id) : IDomainEvent;"));

    [Fact]
    public async Task an_event_named_like_an_intention_is_reported()
    {
        var diagnostic = Assert.Single(await AnalyzerHarness.Analyze("public sealed record ChargePayment(string Id) : IDomainEvent;"));

        Assert.Equal("WYRD0003", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Info, diagnostic.Severity);
    }

    [Fact]
    public async Task the_version_suffix_is_ignored_for_integration_events() =>
        Assert.Empty(await AnalyzerHarness.Analyze(
            "[IntegrationEvent(\"payments.payment-charged\", Version = 3)] public sealed record PaymentChargedV3(string Id) : IIntegrationEvent;",
            "payments.payment-charged v3 | Id: string"));

    [Fact]
    public async Task a_command_named_like_a_fact_is_reported()
    {
        var diagnostic = Assert.Single(await AnalyzerHarness.Analyze("public sealed record PaymentCharged(string Id) : ICommand;"));

        Assert.Equal("WYRD0004", diagnostic.Id);
        Assert.Contains("reads like an event ('Charged')", diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public async Task commands_in_the_imperative_are_clean() =>
        Assert.Empty(await AnalyzerHarness.Analyze("public sealed record ShipOrder(string Id) : ICommand;"));
}
