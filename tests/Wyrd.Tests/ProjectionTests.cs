using Wyrd.Processes;
using Wyrd.Runtime;

namespace Wyrd.Tests;

public class ProjectionTests
{
    private static readonly Process Fulfillment = FluentProcess.OrderFulfillment;

    private readonly InMemoryRuntime _runtime = TestSystem.Create(Fulfillment).AddProjection(new OrderSummaries());

    [Fact]
    public async Task a_view_is_created_and_updated_from_events()
    {
        await _runtime.SendAsync(new PlaceOrder("A-1", Quantity: 1, Amount: 50));
        Assert.Equal(new OrderSummary("A-1", 50, "Paid", Charges: 1), _runtime.View<OrderSummary>("A-1"));

        await _runtime.PublishAsync(new PaymentConfirmed("A-1"));
        Assert.Equal(new OrderSummary("A-1", 50, "Shipped", Charges: 1), _runtime.View<OrderSummary>("A-1"));
    }

    [Fact]
    public async Task returning_null_deletes_the_view()
    {
        await _runtime.SendAsync(new PlaceOrder("A-1", Quantity: 1, Amount: 5_000)); // declined -> stock released

        Assert.Null(_runtime.View<OrderSummary>("A-1"));
    }

    [Fact]
    public async Task views_can_be_listed_in_creation_order()
    {
        await _runtime.SendAsync(new PlaceOrder("A-1", Quantity: 1, Amount: 50));
        await _runtime.SendAsync(new PlaceOrder("A-2", Quantity: 1, Amount: 70));
        await _runtime.PublishAsync(new PaymentConfirmed("A-2"));

        Assert.Equal(
            [new OrderSummary("A-1", 50, "Paid", 1), new OrderSummary("A-2", 70, "Shipped", 1)],
            _runtime.Views<OrderSummary>());
    }

    [Fact]
    public async Task a_retried_command_is_projected_once()
    {
        _runtime.FailNext<ChargePayment>(times: 2);

        await _runtime.SendAsync(new PlaceOrder("A-1", Quantity: 1, Amount: 50));
        await _runtime.AdvanceAsync(5.Seconds);

        Assert.Equal(1, _runtime.View<OrderSummary>("A-1")!.Charges);
    }

    [Fact]
    public void a_view_has_exactly_one_projection() =>
        Assert.Throws<InvalidOperationException>(() => _runtime.AddProjection(new OrderSummaries()));
}
