using Wyrd.Processes;
using Wyrd.Runtime;

namespace Wyrd.Tests;

public class UpcasterTests
{
    private static readonly Process ShipWhenPaidV2 = Process.StartOn<PaymentChargedV2>(e => e.OrderId).Then(Shipping.ShipPaidV2);
    private static readonly Process ShipWhenPaidV3 = Process.StartOn<PaymentChargedV3>(e => e.OrderId).Then(Shipping.ShipPaidV3);

    [Fact]
    public async Task a_subscriber_receives_the_version_it_understands()
    {
        var runtime = TestSystem.Create(ShipWhenPaidV2).AddUpcaster(new PaymentChargedV1ToV2());

        await runtime.SendAsync(new ChargePayment("A-1", 50)); // the publisher still emits V1

        Assert.Contains(new OrderShipped("A-1/EUR"), runtime.History);
        Assert.Contains(runtime.Journal, e => e.Message == "upcast PaymentChargedV1 -> PaymentChargedV2");
    }

    [Fact]
    public async Task upcasters_chain_one_version_at_a_time()
    {
        var runtime = TestSystem.Create(ShipWhenPaidV3)
            .AddUpcaster(new PaymentChargedV1ToV2())
            .AddUpcaster(new PaymentChargedV2ToV3());

        await runtime.SendAsync(new ChargePayment("A-1", 50));

        Assert.Contains(new OrderShipped("A-1/EUR/web"), runtime.History);
    }

    [Fact]
    public async Task during_dual_publishing_a_subscriber_gets_the_fact_once_in_its_version()
    {
        var runtime = TestSystem.Create(ShipWhenPaidV2)
            .AddPublication(new PaymentChargedV2Publication())
            .AddUpcaster(new PaymentChargedV1ToV2());

        await runtime.SendAsync(new ChargePayment("A-1", 50));

        Assert.Single(runtime.History.OfType<OrderShipped>());
        Assert.DoesNotContain(runtime.Journal, e => e.Message.StartsWith("upcast", StringComparison.Ordinal));
        Assert.DoesNotContain(runtime.Journal, e => e.Message.StartsWith("duplicate", StringComparison.Ordinal));
    }

    [Fact]
    public async Task external_facts_are_upcast_too()
    {
        var runtime = TestSystem.Create(ShipWhenPaidV2).AddUpcaster(new PaymentChargedV1ToV2());

        await runtime.PublishAsync(new PaymentChargedV1("B-1", 10));

        Assert.Contains(new OrderShipped("B-1/EUR"), runtime.History);
    }

    [Fact]
    public async Task a_missing_upcaster_fails_loudly()
    {
        var runtime = TestSystem.Create(ShipWhenPaidV2);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.SendAsync(new ChargePayment("A-1", 50)));

        Assert.Contains("expects payments.payment-charged v2", ex.Message, StringComparison.Ordinal);
        Assert.Contains("no upcaster from v1", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task a_newer_version_is_never_downcast()
    {
        var runtime = TestSystem.Create(Process.StartOn<PaymentChargedV1>(e => e.OrderId).Then(Shipping.ShipPaid));

        await runtime.PublishAsync(new PaymentChargedV2("B-1", 10, "USD"));

        Assert.Empty(runtime.History);
    }

    [Fact]
    public void an_upcaster_moves_exactly_one_version_forward()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => new InMemoryRuntime().AddUpcaster(new PaymentChargedV1ToV3()));

        Assert.Contains("maps payments.payment-charged v1 to payments.payment-charged v3", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void there_is_one_upcaster_per_version_step() =>
        Assert.Throws<InvalidOperationException>(() =>
            new InMemoryRuntime().AddUpcaster(new PaymentChargedV1ToV2()).AddUpcaster(new PaymentChargedV1ToV2()));
}
