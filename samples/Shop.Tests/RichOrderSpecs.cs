using System.Collections.Immutable;
using System.Globalization;
using Shop.Contracts;
using Shop.Inventory;
using Shop.Orders;
using Shop.Payments;
using Wyrd;
using Wyrd.Runtime;
using Wyrd.Testing;

namespace Shop.Tests;

public class RichOrderSpecs
{
    private static readonly OrderLine Keyboard = new("keyboard", "KEYBOARD", 2, new Money(80m, "EUR"));
    private static readonly OrderLine Extra = new("extra", "KEYBOARD", 1, new Money(35m, "EUR"));
    private static readonly LineAdded Added = new("A-1", Keyboard);
    private static readonly LineAdded ExtraAdded = new("A-1", Extra);

    [Fact]
    public void the_first_line_starts_a_draft() =>
        Scenario.For(new AddLineSlice())
            .When(new AddLine("A-1", "keyboard", "KEYBOARD", 2, 80m, "eur"))
            .Then(Added)
            .ThenState(new Order.Draft([Keyboard]));

    [Fact]
    public void a_second_line_extends_the_same_consistency_boundary() =>
        Scenario.For(new AddLineSlice())
            .Given(Added)
            .When(new AddLine("A-1", "extra", "KEYBOARD", 1, 35m))
            .Then(ExtraAdded)
            .ThenState(new Order.Draft([Keyboard, Extra]));

    [Theory]
    [InlineData(null, "KEYBOARD", 1)]
    [InlineData("", "KEYBOARD", 1)]
    [InlineData(" ", "KEYBOARD", 1)]
    [InlineData("line", null, 1)]
    [InlineData("line", " ", 1)]
    [InlineData("line", "KEYBOARD", 0)]
    [InlineData("line", "KEYBOARD", -1)]
    public void invalid_line_input_is_a_rejection(string? lineId, string? sku, int quantity) =>
        Scenario.For(new AddLineSlice())
            .When(new AddLine("A-1", lineId!, sku!, quantity, 10m))
            .ThenRejected(OrderError.InvalidLine);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("EU")]
    [InlineData("E1R")]
    public void invalid_currencies_are_rejected_before_constructing_money(string? currency) =>
        Scenario.For(new AddLineSlice())
            .When(new AddLine("A-1", "line", "KEYBOARD", 1, 10m, currency!))
            .ThenRejected(OrderError.InvalidMoney);

    [Fact]
    public void a_negative_line_amount_is_rejected() =>
        Scenario.For(new AddLineSlice())
            .When(new AddLine("A-1", "line", "KEYBOARD", 1, -1m))
            .ThenRejected(OrderError.InvalidMoney);

    [Fact]
    public void an_order_never_adds_amounts_in_different_currencies() =>
        Scenario.For(new AddLineSlice())
            .Given(Added)
            .When(new AddLine("A-1", "extra", "KEYBOARD", 1, 10m, "USD"))
            .ThenRejected(OrderError.CurrencyMismatch);

    [Fact]
    public void cancelling_all_lines_does_not_change_the_orders_currency() =>
        Scenario.For(new AddLineSlice())
            .Given(Added, new LineCancelled("A-1", Keyboard.LineId))
            .When(new AddLine("A-1", "extra", "KEYBOARD", 1, 10m, "USD"))
            .ThenRejected(OrderError.CurrencyMismatch);

    [Fact]
    public void a_total_that_cannot_be_represented_is_a_domain_rejection() =>
        Scenario.For(new AddLineSlice())
            .Given(new LineAdded("A-1", new OrderLine("max", "KEYBOARD", 1, new Money(decimal.MaxValue, "EUR"))))
            .When(new AddLine("A-1", "extra", "KEYBOARD", 1, 1m))
            .ThenRejected(OrderError.TotalTooLarge);

    [Fact]
    public void a_line_identity_cannot_be_reused_even_after_cancellation() =>
        Scenario.For(new AddLineSlice())
            .Given(Added, new LineCancelled("A-1", Keyboard.LineId))
            .When(new AddLine("A-1", Keyboard.LineId, "MOUSE", 1, 20m))
            .ThenRejected(OrderError.DuplicateLine);

    [Fact]
    public void the_order_is_bounded_even_when_lines_have_been_cancelled()
    {
        var lines = Enumerable.Range(1, Order.MaxLines)
            .Select(id => new OrderLine(id.ToString(CultureInfo.InvariantCulture), "KEYBOARD", 1, new Money(1m, "EUR"), isCancelled: true))
            .ToImmutableList();
        Scenario.For(new AddLineSlice())
            .GivenState(new Order.Draft(lines))
            .When(new AddLine("A-1", "extra", "KEYBOARD", 1, 1m))
            .ThenRejected(OrderError.TooManyLines);
    }

    [Fact]
    public void the_last_allowed_line_is_accepted()
    {
        var lines = Enumerable.Range(1, Order.MaxLines - 1)
            .Select(id => new OrderLine(id.ToString(CultureInfo.InvariantCulture), "KEYBOARD", 1, new Money(1m, "EUR")))
            .ToImmutableList();
        Scenario.For(new AddLineSlice())
            .GivenState(new Order.Draft(lines))
            .When(new AddLine("A-1", "extra", "KEYBOARD", 1, 1m))
            .ThenState(new Order.Draft(lines.Add(new OrderLine("extra", "KEYBOARD", 1, new Money(1m, "EUR")))));
    }

    [Fact]
    public void cancelling_a_line_keeps_its_identity_and_other_lines() =>
        Scenario.For(new CancelLineSlice())
            .Given(Added, ExtraAdded)
            .When(new CancelLine("A-1", Keyboard.LineId))
            .Then(new LineCancelled("A-1", Keyboard.LineId))
            .ThenState(new Order.Draft([Keyboard.Cancel(), Extra]));

    [Fact]
    public void cancelling_a_missing_line_is_rejected() =>
        Scenario.For(new CancelLineSlice())
            .Given(Added)
            .When(new CancelLine("A-1", "missing"))
            .ThenRejected(OrderError.LineNotFound);

    [Fact]
    public void cancelling_a_line_twice_is_rejected() =>
        Scenario.For(new CancelLineSlice())
            .Given(Added, new LineCancelled("A-1", Keyboard.LineId))
            .When(new CancelLine("A-1", Keyboard.LineId))
            .ThenRejected(OrderError.LineAlreadyCancelled);

    [Fact]
    public void cancelling_the_order_preserves_its_lines_and_amounts() =>
        Scenario.For(new CancelDraftOrderSlice())
            .Given(Added, ExtraAdded)
            .When(new CancelDraftOrder("A-1"))
            .Then(new OrderCancelled("A-1"))
            .ThenState(new Order.Cancelled([Keyboard, Extra]));

    [Fact]
    public void a_cancelled_order_rejects_every_write_path()
    {
        var history = new IDomainEvent[] { Added, new OrderCancelled("A-1") };
        Scenario.For(new AddLineSlice()).Given(history)
            .When(new AddLine("A-1", "extra", "KEYBOARD", 1, 1m)).ThenRejected(OrderError.AlreadyCancelled);
        Scenario.For(new CancelLineSlice()).Given(history)
            .When(new CancelLine("A-1", Keyboard.LineId)).ThenRejected(OrderError.AlreadyCancelled);
        Scenario.For(new SubmitOrderSlice()).Given(history)
            .When(new SubmitOrder("A-1")).ThenRejected(OrderError.AlreadyCancelled);
        Scenario.For(new PlaceOrderSlice()).Given(history)
            .When(new PlaceOrder("A-1", "KEYBOARD", 1, 1m)).ThenRejected(OrderError.AlreadyCancelled);
        Scenario.For(new CancelDraftOrderSlice()).Given(history)
            .When(new CancelDraftOrder("A-1")).ThenRejected(OrderError.AlreadyCancelled);
        Scenario.For(new CancelOrderSlice()).Given(history)
            .When(new CancelOrder("A-1")).ThenRejected(OrderError.AlreadyCancelled);
    }

    [Fact]
    public void a_placed_order_is_frozen_for_user_edits_and_cancellation()
    {
        var placed = new OrderPlaced("A-1", "KEYBOARD", 2, 80m);
        Scenario.For(new AddLineSlice()).Given(placed)
            .When(new AddLine("A-1", "extra", "KEYBOARD", 1, 1m)).ThenRejected(OrderError.AlreadyPlaced);
        Scenario.For(new CancelLineSlice()).Given(placed)
            .When(new CancelLine("A-1", "initial")).ThenRejected(OrderError.AlreadyPlaced);
        Scenario.For(new CancelDraftOrderSlice()).Given(placed)
            .When(new CancelDraftOrder("A-1")).ThenRejected(OrderError.AlreadyPlaced);
    }

    [Fact]
    public void the_original_checkout_cannot_overwrite_a_draft() =>
        Scenario.For(new PlaceOrderSlice())
            .Given(Added)
            .When(new PlaceOrder("A-1", "MOUSE", 5, 10m))
            .ThenRejected(OrderError.DraftExists);

    [Fact]
    public void the_original_checkout_preserves_an_indivisible_line_total() =>
        Scenario.For(new PlaceOrderSlice())
            .When(new PlaceOrder("A-1", "KEYBOARD", 3, 1m))
            .Then(new OrderPlaced("A-1", "KEYBOARD", 3, 1m))
            .ThenState(new Order.Placed([new OrderLine("initial", "KEYBOARD", 3, new Money(1m, "EUR"))]));

    [Fact]
    public void the_original_checkout_rejects_a_negative_amount() =>
        Scenario.For(new PlaceOrderSlice())
            .When(new PlaceOrder("A-1", "KEYBOARD", 1, -1m))
            .ThenRejected(OrderError.InvalidMoney);

    [Fact]
    public void submitting_sums_active_lines_and_freezes_all_identities() =>
        Scenario.For(new SubmitOrderSlice())
            .Given(Added, ExtraAdded, new LineCancelled("A-1", Keyboard.LineId))
            .When(new SubmitOrder("A-1"))
            .Then(new OrderPlaced("A-1", "KEYBOARD", 1, 35m, [Keyboard.Cancel(), Extra]))
            .ThenState(new Order.Placed([Keyboard.Cancel(), Extra]));

    [Fact]
    public void an_empty_draft_cannot_start_fulfillment() =>
        Scenario.For(new SubmitOrderSlice())
            .Given(Added, new LineCancelled("A-1", Keyboard.LineId))
            .When(new SubmitOrder("A-1"))
            .ThenRejected(OrderError.EmptyOrder);

    [Fact]
    public void multiple_skus_cannot_be_sent_to_the_single_sku_fulfillment() =>
        Scenario.For(new SubmitOrderSlice())
            .Given(Added, new LineAdded("A-1", new OrderLine("mouse", "MOUSE", 1, new Money(20m, "EUR"))))
            .When(new SubmitOrder("A-1"))
            .ThenRejected(OrderError.MultipleSkusNotSupported);

    [Fact]
    public void unsupported_settlement_currency_is_explicit() =>
        Scenario.For(new SubmitOrderSlice())
            .Given(new LineAdded("A-1", new OrderLine("keyboard", "KEYBOARD", 1, new Money(20m, "USD"))))
            .When(new SubmitOrder("A-1"))
            .ThenRejected(OrderError.CurrencyNotSupported);

    [Fact]
    public void combined_quantity_overflow_is_a_rejection() =>
        Scenario.For(new SubmitOrderSlice())
            .Given(new LineAdded("A-1", new OrderLine("max", "KEYBOARD", int.MaxValue, new Money(1m, "EUR"))), ExtraAdded)
            .When(new SubmitOrder("A-1"))
            .ThenRejected(OrderError.QuantityTooLarge);

    [Fact]
    public void commands_that_require_an_existing_order_reject_a_missing_key()
    {
        Scenario.For(new CancelLineSlice()).When(new CancelLine("missing", "line")).ThenRejected(OrderError.NotFound);
        Scenario.For(new SubmitOrderSlice()).When(new SubmitOrder("missing")).ThenRejected(OrderError.NotFound);
        Scenario.For(new CancelDraftOrderSlice()).When(new CancelDraftOrder("missing")).ThenRejected(OrderError.NotFound);
        Scenario.For(new CancelOrderSlice()).When(new CancelOrder("missing")).ThenRejected(OrderError.NotFound);
    }

    [Fact]
    public async Task a_rich_draft_enters_the_existing_process_only_when_submitted()
    {
        using var shop = ShopModule.CreateRuntime();
        await shop.SendAsync(new Restock("KEYBOARD", 5));
        await shop.SendAsync(new AddLine("A-1", "keyboard", "KEYBOARD", 2, 80m));
        await shop.SendAsync(new AddLine("A-1", "extra", "KEYBOARD", 1, 35m));
        await shop.SendAsync(new CancelLine("A-1", "extra"));
        Assert.Null(shop.StatusOf(Shop.Fulfillment.Processes.OrderFulfillment, "A-1"));
        Assert.Equal(5, shop.StateOf<StockItem>("KEYBOARD").Available);
        Assert.Empty(shop.Published);

        await shop.SendAsync(new SubmitOrder("A-1"));
        Assert.Equal(new Payment.Charged(80m), shop.StateOf<Payment>("A-1"));
        Assert.Equal(3, shop.StateOf<StockItem>("KEYBOARD").Available);
        Assert.Equal(Order.LinesOf(new OrderPlaced("A-1", "KEYBOARD", 2, 80m, [Keyboard, Extra.Cancel()])), shop.StateOf<Order>("A-1").Lines);
        Assert.Equal(new Money(80m, "EUR"), shop.View<OrderDetail>("A-1")!.Total);
        Assert.Contains(new PaymentChargedV1("A-1", 80m, "EUR"), shop.Published);

        var rejected = await shop.SendAsync(new CancelDraftOrder("A-1"));
        Assert.IsType<Decision.Rejected>(rejected);
        await shop.PublishAsync(new PaymentConfirmed("A-1"));
        Assert.Equal(ProcessStatus.Completed, shop.StatusOf(Shop.Fulfillment.Processes.OrderFulfillment, "A-1"));
        Assert.Equal("Shipped", shop.View<OrderSummary>("A-1")!.Status);
        Assert.Equal(3, shop.StateOf<StockItem>("KEYBOARD").Available);
    }

    [Fact]
    public async Task rich_orders_preserve_stock_money_and_snapshots_in_hostile_futures()
    {
        async Task Afternoon(InMemoryRuntime shop)
        {
            await shop.SendAsync(new Restock("KEYBOARD", 5));
            await shop.SendAsync(new AddLine("A-1", "keyboard", "KEYBOARD", 2, 80m));
            await shop.SendAsync(new AddLine("A-1", "extra", "KEYBOARD", 1, 35m));
            await shop.SendAsync(new CancelLine("A-1", "extra"));
            await shop.SendAsync(new SubmitOrder("A-1"));
            await shop.PublishAsync(new PaymentConfirmed("A-1"));
        }

        var conserved = new Invariant("rich order stock and price are conserved", shop =>
        {
            var stock = shop.StateOf<StockItem>("KEYBOARD");
            var shipped = shop.View<OrderSummary>("A-1")!.Status == "Shipped";
            var held = shop.StatusOf(Shop.Fulfillment.Processes.OrderFulfillment, "A-1") == ProcessStatus.Suspended
                ? stock.Reservations.Values.Sum() : 0;
            return stock.Available + held + (shipped ? 2 : 0) == 5
                && shop.StateOf<Order>("A-1").Total == new Money(80m, "EUR")
                && shop.History.OfType<PaymentCharged>().All(charged => charged.Amount == 80m)
                    ? null : $"available {stock.Available}, held by suspended order {held}, shipped {shipped}; stock, charge or frozen price changed";
        });
        var report = await Skuld.SimulateAsync(ShopModule.CreateRuntime, Afternoon,
            [Invariant.ProcessesSettle, conserved], new SimulationOptions { Runs = 500, FaultRate = .2, DuplicateRate = .2, ReorderRate = .5 });
        report.ThrowIfViolated();
    }
}
