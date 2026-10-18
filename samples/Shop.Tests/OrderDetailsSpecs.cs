using System.Collections.Immutable;
using Shop.Orders;
using Wyrd.Testing;

namespace Shop.Tests;

public class OrderDetailsSpecs
{
    [Fact]
    public void adding_the_first_line_creates_a_draft()
    {
        var line = new OrderLine("line-1", "KEYBOARD", 2, new Money(89m, "EUR"));

        Scenario.For(new OrderDetails())
            .Given(new LineAdded("A-1", line))
            .Then("A-1", new OrderDetail("A-1", [line], new Money(89m, "EUR"), "Draft"));
    }

    [Fact]
    public void a_draft_shows_all_line_totals()
    {
        var keyboard = new OrderLine("line-1", "KEYBOARD", 2, new Money(89m, "EUR"));
        var mouse = new OrderLine("line-2", "MOUSE", 1, new Money(25.5m, "EUR"));

        Scenario.For(new OrderDetails())
            .Given(new LineAdded("A-1", keyboard), new LineAdded("A-1", mouse))
            .Then("A-1", new OrderDetail("A-1", [keyboard, mouse], new Money(114.5m, "EUR"), "Draft"));
    }

    [Fact]
    public void cancelling_a_line_retains_its_identity_and_removes_its_amount_from_the_total()
    {
        var keyboard = new OrderLine("line-1", "KEYBOARD", 2, new Money(89m, "EUR"));
        var mouse = new OrderLine("line-2", "MOUSE", 1, new Money(25.5m, "EUR"));

        Scenario.For(new OrderDetails())
            .Given(new LineAdded("A-1", keyboard), new LineAdded("A-1", mouse), new LineCancelled("A-1", keyboard.LineId))
            .Then("A-1", new OrderDetail("A-1", [keyboard.Cancel(), mouse], new Money(25.5m, "EUR"), "Draft"));
    }

    [Fact]
    public void cancelling_the_last_line_leaves_a_zero_total_in_the_order_currency()
    {
        var line = new OrderLine("line-1", "KEYBOARD", 2, new Money(89m, "USD"));

        Scenario.For(new OrderDetails())
            .Given(new LineAdded("A-1", line), new LineCancelled("A-1", line.LineId))
            .Then("A-1", new OrderDetail("A-1", [line.Cancel()], new Money(0m, "USD"), "Draft"));
    }

    [Fact]
    public void a_legacy_placement_becomes_one_line_without_rounding_the_amount() =>
        Scenario.For(new OrderDetails())
            .Given(new OrderPlaced("A-1", "KEYBOARD", 3, 10m))
            .Then("A-1", new OrderDetail("A-1",
                [new OrderLine("initial", "KEYBOARD", 3, new Money(10m, "EUR"))], new Money(10m, "EUR"), "Placed"));

    [Fact]
    public void placement_installs_the_frozen_rich_order_snapshot()
    {
        var line = new OrderLine("line-1", "KEYBOARD", 2, new Money(89m, "EUR"));
        var cancelled = new OrderLine("line-2", "MOUSE", 1, new Money(25.5m, "EUR")).Cancel();
        ImmutableList<OrderLine> lines = [line, cancelled];

        Scenario.For(new OrderDetails())
            .Given(new LineAdded("A-1", line), new OrderPlaced("A-1", "KEYBOARD", 2, 89m, lines))
            .Then("A-1", new OrderDetail("A-1", lines, new Money(89m, "EUR"), "Placed"));
    }

    [Fact]
    public void cancelling_a_draft_preserves_its_lines_and_total()
    {
        var line = new OrderLine("line-1", "KEYBOARD", 2, new Money(89m, "EUR"));

        Scenario.For(new OrderDetails())
            .Given(new LineAdded("A-1", line), new OrderCancelled("A-1"))
            .Then("A-1", new OrderDetail("A-1", [line], new Money(89m, "EUR"), "Cancelled"));
    }

    [Fact]
    public void cancelling_a_placed_order_preserves_its_lines_and_total() =>
        Scenario.For(new OrderDetails())
            .Given(new OrderPlaced("A-1", "KEYBOARD", 3, 10m), new OrderCancelled("A-1"))
            .Then("A-1", new OrderDetail("A-1",
                [new OrderLine("initial", "KEYBOARD", 3, new Money(10m, "EUR"))], new Money(10m, "EUR"), "Cancelled"));
}
