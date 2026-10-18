namespace Wyrd.Analyzers.Tests;

public class MessageRuleTests
{
    [Fact]
    public async Task a_message_cannot_be_both_a_command_and_an_event()
    {
        var diagnostic = Assert.Single(await AnalyzerHarness.Analyze(
            "public sealed record ChargePayment(string OrderId) : ICommand, IDomainEvent;"));

        Assert.Equal("WYRD0001", diagnostic.Id);
    }

    [Fact]
    public async Task an_integration_event_cannot_expose_a_domain_event()
    {
        const string source = """
            public sealed record PaymentCharged(string OrderId) : IDomainEvent;

            [IntegrationEvent("payments.payment-charged")]
            public sealed record PaymentChargedV1(PaymentCharged Payment) : IIntegrationEvent;
            """;

        var diagnostics = await AnalyzerHarness.Analyze(source, "payments.payment-charged v1 | Payment: PaymentCharged");

        var leak = Assert.Single(diagnostics, d => d.Id == "WYRD0101");
        Assert.Contains("exposes 'PaymentCharged' through 'Payment'", leak.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public async Task domain_types_are_found_inside_collections()
    {
        const string source = """
            public sealed record ShipOrder(string OrderId) : ICommand;

            [IntegrationEvent("shipping.batch")]
            public sealed record BatchV1(System.Collections.Generic.IReadOnlyList<ShipOrder> Orders) : IIntegrationEvent;
            """;

        var diagnostics = await AnalyzerHarness.Analyze(source, "shipping.batch v1 | Orders: IReadOnlyList<ShipOrder>");

        Assert.Single(diagnostics, d => d.Id == "WYRD0101");
    }

    [Fact]
    public async Task an_integration_event_needs_its_attribute() =>
        Assert.Equal("WYRD0102", Assert.Single(await AnalyzerHarness.Analyze(
            "public sealed record PaymentChargedV1(string OrderId) : IIntegrationEvent;")).Id);

    [Fact]
    public async Task the_attribute_needs_the_interface() =>
        Assert.Equal("WYRD0102", Assert.Single(await AnalyzerHarness.Analyze(
            "[IntegrationEvent(\"payments.payment-charged\")] public sealed record PaymentChargedV1(string OrderId);")).Id);

    [Fact]
    public async Task commands_and_domain_events_need_no_snapshot() =>
        Assert.Empty(await AnalyzerHarness.Analyze("""
            public sealed record ChargePayment(string OrderId) : ICommand;
            public sealed record PaymentCharged(string OrderId) : IDomainEvent;
            """));
}
