using Wyrd.Testing;

namespace Wyrd.Tests;

// A state that can't be stored: it holds an interface.
public sealed record LeakyState(IDomainEvent? Last) : IState<LeakyState>
{
    public static LeakyState Initial { get; } = new((IDomainEvent?)null);

    public LeakyState Evolve(IDomainEvent domainEvent) => new(domainEvent);
}

public sealed class LeakySlice : Slice<ShipOrder, LeakyState, OrderShipped, StockError>
{
    public override object KeyOf(ShipOrder command) => command.OrderId;

    public override Result<OrderShipped, StockError> Decide(ShipOrder command, LeakyState state) => new OrderShipped(command.OrderId);
}

public class ScenarioTests
{
    [Fact]
    public void an_accepted_command_produces_the_expected_event() =>
        Scenario.For(new ChargePaymentSlice())
            .When(new ChargePayment("A-1", 50))
            .Then(new PaymentCharged("A-1", 50))
            .ThenState(new EventLog(["PaymentCharged"]));

    [Fact]
    public void a_rejected_command_produces_the_expected_error() =>
        Scenario.For(new ChargePaymentSlice())
            .When(new ChargePayment("A-1", 5_000))
            .ThenRejected(PaymentError.Declined);

    [Fact]
    public void given_rebuilds_the_state_from_history() =>
        Scenario.For(new PlaceOrderSlice())
            .Given(new OrderPlaced("A-1", 1, 50))
            .When(new PlaceOrder("A-1", 1, 50))
            .ThenRejected(OrderError.AlreadyExists);

    [Fact]
    public void a_different_event_fails_with_both_values()
    {
        var ex = Assert.Throws<WyrdAssertionException>(() =>
            Scenario.For(new ChargePaymentSlice()).When(new ChargePayment("A-1", 50)).Then(new PaymentCharged("A-1", 60)));

        Assert.Equal(
            "Expected ChargePayment to produce PaymentCharged { OrderId = A-1, Amount = 60 }, but it produced PaymentCharged { OrderId = A-1, Amount = 50 }.",
            ex.Message);
    }

    [Fact]
    public void expecting_a_rejection_that_does_not_happen_fails()
    {
        var ex = Assert.Throws<WyrdAssertionException>(() =>
            Scenario.For(new ChargePaymentSlice()).When(new ChargePayment("A-1", 50)).ThenRejected(PaymentError.Declined));

        Assert.StartsWith("Expected ChargePayment to be rejected with Declined, but it was accepted", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void expecting_an_event_when_the_command_is_rejected_fails()
    {
        var ex = Assert.Throws<WyrdAssertionException>(() =>
            Scenario.For(new ChargePaymentSlice()).When(new ChargePayment("A-1", 5_000)).Then(new PaymentCharged("A-1", 5_000)));

        Assert.EndsWith("but it was rejected with Declined.", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void a_state_that_cannot_be_stored_is_caught_in_a_unit_test()
    {
        var ex = Assert.Throws<WyrdAssertionException>(() =>
            Scenario.For(new LeakySlice()).When(new ShipOrder("A-1")).Then(new OrderShipped("A-1")));

        Assert.StartsWith("State LeakyState cannot be stored", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void a_projection_scenario_checks_the_view() =>
        Scenario.For(new OrderSummaries())
            .Given(new OrderPlaced("A-1", 1, 50), new PaymentCharged("A-1", 50), new OrderShipped("A-1"))
            .Then("A-1", new OrderSummary("A-1", 50, "Shipped", Charges: 1))
            .Then("A-2", null);

    [Fact]
    public void a_projection_can_delete_its_view() =>
        Scenario.For(new OrderSummaries())
            .Given(new OrderPlaced("A-1", 1, 50), new StockReleased("A-1"))
            .Then("A-1", null);

    [Fact]
    public void a_wrong_view_fails_with_both_values()
    {
        var ex = Assert.Throws<WyrdAssertionException>(() =>
            Scenario.For(new OrderSummaries()).Given(new OrderPlaced("A-1", 1, 50)).Then("A-1", new OrderSummary("A-1", 50, "Shipped")));

        Assert.Contains("but it is OrderSummary { OrderId = A-1, Amount = 50, Status = Placed, Charges = 0 }", ex.Message, StringComparison.Ordinal);
    }
}
