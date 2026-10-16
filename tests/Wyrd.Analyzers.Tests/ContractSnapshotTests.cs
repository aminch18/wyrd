namespace Wyrd.Analyzers.Tests;

public class ContractSnapshotTests
{
    private const string ChargedV1 = """
        [IntegrationEvent("payments.payment-charged", Version = 1)]
        public sealed record PaymentChargedV1(string OrderId, decimal Amount) : IIntegrationEvent;
        """;

    private const string Snapshot = "payments.payment-charged v1 | Amount: decimal; OrderId: string";

    [Fact]
    public async Task a_contract_matching_its_snapshot_is_clean() =>
        Assert.Empty(await AnalyzerHarness.Analyze(ChargedV1, Snapshot));

    [Fact]
    public async Task a_new_contract_must_be_added_to_the_snapshot()
    {
        var diagnostic = Assert.Single(await AnalyzerHarness.Analyze(ChargedV1, contracts: ""));

        Assert.Equal("WYRD0200", diagnostic.Id);
        Assert.Contains($"'{Snapshot}'", diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public async Task without_a_snapshot_file_every_contract_is_reported() =>
        Assert.Equal("WYRD0200", Assert.Single(await AnalyzerHarness.Analyze(ChargedV1)).Id);

    [Fact]
    public async Task changing_a_field_type_is_a_breaking_change()
    {
        const string source = """
            [IntegrationEvent("payments.payment-charged", Version = 1)]
            public sealed record PaymentChargedV1(string OrderId, double Amount) : IIntegrationEvent;
            """;

        var diagnostic = Assert.Single(await AnalyzerHarness.Analyze(source, Snapshot));

        Assert.Equal("WYRD0201", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("field 'Amount' changed type from decimal to double", diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.Contains("Publish it as v2", diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public async Task removing_or_renaming_a_field_is_a_breaking_change()
    {
        const string source = """
            [IntegrationEvent("payments.payment-charged", Version = 1)]
            public sealed record PaymentChargedV1(string OrderId, decimal Total) : IIntegrationEvent;
            """;

        var diagnostic = Assert.Single(await AnalyzerHarness.Analyze(source, Snapshot));

        Assert.Equal("WYRD0201", diagnostic.Id);
        Assert.Contains("field 'Amount' was removed or renamed", diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.Contains("required field 'Total' was added", diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public async Task adding_an_optional_field_is_compatible_but_the_snapshot_must_be_updated()
    {
        const string source = """
            [IntegrationEvent("payments.payment-charged", Version = 1)]
            public sealed record PaymentChargedV1(string OrderId, decimal Amount, string? Currency = null) : IIntegrationEvent;
            """;

        var diagnostic = Assert.Single(await AnalyzerHarness.Analyze(source, Snapshot));

        Assert.Equal("WYRD0202", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("Amount: decimal; Currency: string?; OrderId: string", diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public async Task publishing_a_new_version_alongside_the_old_one_is_clean()
    {
        const string source = ChargedV1 + """

            [IntegrationEvent("payments.payment-charged", Version = 2)]
            public sealed record PaymentChargedV2(string OrderId, decimal Amount, string Currency) : IIntegrationEvent;
            """;
        const string snapshot = Snapshot + "\npayments.payment-charged v2 | Amount: decimal; Currency: string; OrderId: string";

        Assert.Empty(await AnalyzerHarness.Analyze(source, snapshot));
    }

    [Fact]
    public async Task removing_a_published_contract_is_an_error()
    {
        var diagnostic = Assert.Single(await AnalyzerHarness.Analyze("public sealed class Nothing;", Snapshot));

        Assert.Equal("WYRD0203", diagnostic.Id);
        Assert.Equal("contracts.wyrd.txt", diagnostic.Location.GetLineSpan().Path);
    }

    [Fact]
    public async Task a_retired_contract_can_be_removed() =>
        Assert.Empty(await AnalyzerHarness.Analyze("public sealed class Nothing;", "retired " + Snapshot));

    [Fact]
    public async Task comments_and_blank_lines_are_ignored() =>
        Assert.Empty(await AnalyzerHarness.Analyze(ChargedV1, "# Payments\n\n" + Snapshot + "\n"));

    [Fact]
    public async Task a_name_and_version_identify_exactly_one_contract()
    {
        const string source = ChargedV1 + """

            [IntegrationEvent("payments.payment-charged", Version = 1)]
            public sealed record PaymentChargedCopy(string OrderId, decimal Amount) : IIntegrationEvent;
            """;

        var diagnostics = await AnalyzerHarness.Analyze(source, Snapshot);

        Assert.Equal(2, diagnostics.Count(d => d.Id == "WYRD0204"));
    }
}
