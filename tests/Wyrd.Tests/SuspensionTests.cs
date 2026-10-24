using Wyrd.Processes;
using Wyrd.Runtime;

namespace Wyrd.Tests;

public class SuspensionTests
{
    private static readonly Process Fulfillment = FluentProcess.OrderFulfillment;

    private readonly InMemoryRuntime _runtime = TestSystem.Create(Fulfillment);

    private async Task SuspendOnChargeAsync()
    {
        _runtime.FailNext<ChargePayment>(times: 4); // one attempt + Retry(3.Times)
        await _runtime.SendAsync(new PlaceOrder("A-1", Quantity: 1, Amount: 50));
        await _runtime.AdvanceAsync(10.Seconds);
        Assert.Equal(ProcessStatus.Suspended, _runtime.StatusOf(Fulfillment, "A-1"));
    }

    [Fact]
    public async Task resuming_after_the_fix_carries_on_where_it_stopped()
    {
        await SuspendOnChargeAsync();

        await _runtime.ResumeAsync(Fulfillment, "A-1");
        await _runtime.PublishAsync(new PaymentConfirmed("A-1"));

        Assert.Equal(ProcessStatus.Completed, _runtime.StatusOf(Fulfillment, "A-1"));
        Assert.Null(_runtime.ReasonOf(Fulfillment, "A-1"));
        Assert.Single(_runtime.History.OfType<PaymentCharged>());
    }

    [Fact]
    public async Task compensating_undoes_what_was_done_and_fences_the_step()
    {
        await SuspendOnChargeAsync();

        await _runtime.CompensateAsync(Fulfillment, "A-1");

        Assert.Equal(ProcessStatus.Compensated, _runtime.StatusOf(Fulfillment, "A-1"));
        Assert.Contains(new StockReleased("A-1"), _runtime.History);
        Assert.Contains(_runtime.Journal, e => e.Message == "nothing to undo: Payment.Charge never ran");
    }

    [Fact]
    public async Task an_undo_that_gave_up_can_be_resumed()
    {
        _runtime.FailNext<ReleaseStock>(times: 11);
        await _runtime.SendAsync(new PlaceOrder("A-1", Quantity: 1, Amount: 5_000)); // declined
        await _runtime.AdvanceAsync(10.Minutes);
        Assert.Equal(ProcessStatus.Suspended, _runtime.StatusOf(Fulfillment, "A-1"));

        await _runtime.ResumeAsync(Fulfillment, "A-1");

        Assert.Equal(ProcessStatus.Compensated, _runtime.StatusOf(Fulfillment, "A-1"));
        Assert.Contains(new StockReleased("A-1"), _runtime.History);
    }

    [Fact]
    public async Task an_instance_that_was_compensating_is_finished_by_resuming_it()
    {
        _runtime.FailNext<ReleaseStock>(times: 11);
        await _runtime.SendAsync(new PlaceOrder("A-1", Quantity: 1, Amount: 5_000));
        await _runtime.AdvanceAsync(10.Minutes);

        var ex = await Assert.ThrowsAsync<InstanceStateException>(() => _runtime.CompensateAsync(Fulfillment, "A-1"));

        Assert.Contains("resume it to finish the compensation", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task only_a_suspended_instance_can_be_resumed_or_compensated()
    {
        await _runtime.SendAsync(new PlaceOrder("A-1", Quantity: 1, Amount: 50)); // awaiting confirmation

        await Assert.ThrowsAsync<InstanceStateException>(() => _runtime.ResumeAsync(Fulfillment, "A-1"));
        await Assert.ThrowsAsync<InstanceStateException>(() => _runtime.CompensateAsync(Fulfillment, "B-404"));
    }

    [Fact]
    public async Task a_suspended_instance_keeps_its_effects_visible_until_someone_decides()
    {
        await SuspendOnChargeAsync();
        await _runtime.AdvanceAsync(1.Days);

        Assert.Equal(ProcessStatus.Suspended, _runtime.StatusOf(Fulfillment, "A-1"));
        Assert.DoesNotContain(new StockReleased("A-1"), _runtime.History);
    }
}
