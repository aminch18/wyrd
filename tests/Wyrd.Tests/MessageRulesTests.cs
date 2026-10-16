using Wyrd.Runtime;

namespace Wyrd.Tests;

public class MessageRulesTests
{
    [Fact]
    public void a_command_has_exactly_one_owner()
    {
        var runtime = new InMemoryRuntime().AddSlice(new ChargePaymentSlice());

        var ex = Assert.Throws<InvalidOperationException>(() => runtime.AddSlice(new ChargePaymentSlice()));

        Assert.Contains("A command has exactly one owner", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void a_command_without_owner_cannot_be_sent()
    {
        var runtime = new InMemoryRuntime();

        var ex = Assert.Throws<InvalidOperationException>(() => runtime.Send(new ChargePayment("A-1", 50)));

        Assert.Contains("No slice owns ChargePayment", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void domain_events_stay_internal_unless_explicitly_published()
    {
        var runtime = TestSystem.Create();

        runtime.Send(new ReserveStock("A-1", 1));

        Assert.Equal([new StockReserved("A-1")], runtime.History);
        Assert.Empty(runtime.Published);
    }

    [Fact]
    public void a_publication_turns_a_domain_event_into_an_integration_event()
    {
        var runtime = TestSystem.Create();

        runtime.Send(new ChargePayment("A-1", 50));

        Assert.Equal([new PaymentChargedV1("A-1", 50)], runtime.Published);
    }

    [Fact]
    public void two_contract_versions_can_be_published_during_a_migration()
    {
        var runtime = TestSystem.Create().AddPublication(new PaymentChargedV2Publication());

        runtime.Send(new ChargePayment("A-1", 50));

        Assert.Equal<IIntegrationEvent>(
            [new PaymentChargedV1("A-1", 50), new PaymentChargedV2("A-1", 50, "EUR")],
            runtime.Published);
    }

    [Fact]
    public void rejected_commands_publish_nothing()
    {
        var runtime = TestSystem.Create();

        runtime.Send(new ChargePayment("A-1", 5_000));

        Assert.Empty(runtime.History);
        Assert.Empty(runtime.Published);
    }
}
