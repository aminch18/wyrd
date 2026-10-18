using Wyrd.Processes;
using Wyrd.Runtime;

namespace Wyrd.Tests;

public class FingerprintTests
{
    private static Process Fulfillment => FluentProcess.OrderFulfillment;

    // Same name, a step inserted before the wait: an in-flight instance would continue at the wrong step.
    private static Process Reshaped => Process.StartOn<OrderPlaced>(e => e.OrderId, Fulfillment.Name)
        .Then(Stock.Reserve).Retry(3.Times).Undo(Stock.Release)
        .Then(Payment.Charge).Retry(3.Times).Undo(Payment.Refund)
        .Then(Payment.Charge).NoRetry("only here to change the shape")
        .Await<PaymentConfirmed>(e => e.OrderId).Within(15.Minutes)
        .Then(Shipping.Ship);

    [Fact]
    public void the_fingerprint_is_the_structure_not_the_configuration()
    {
        Process retuned = Process.StartOn<OrderPlaced>(e => e.OrderId, Fulfillment.Name)
            .Then(Stock.Reserve).Retry(10.Times, Backoff.Linear).Undo(Stock.Release, 20.Times)
            .Then(Payment.Charge).NoRetry("tuned").Undo(Payment.Refund)
            .Await<PaymentConfirmed>(e => e.OrderId).Within(1.Hours)
            .Then(Shipping.Ship).Retry(2.Times);

        Assert.Equal(Fulfillment.Fingerprint, FluentProcess.OrderFulfillment.Fingerprint);
        Assert.Equal(Fulfillment.Fingerprint, retuned.Fingerprint);
        Assert.NotEqual(Fulfillment.Fingerprint, Reshaped.Fingerprint);
        Assert.Matches("^[0-9a-f]{12}$", Fulfillment.Fingerprint);
    }

    [Fact]
    public async Task an_instance_whose_definition_changed_is_suspended_instead_of_continuing()
    {
        var runtime = TestSystem.Create(Fulfillment);
        await runtime.SendAsync(new PlaceOrder("A-1", Quantity: 1, Amount: 50)); // awaiting the confirmation

        runtime.Redeploy(Reshaped);
        await runtime.PublishAsync(new PaymentConfirmed("A-1"));

        Assert.Equal(ProcessStatus.Suspended, runtime.StatusOf(Fulfillment, "A-1"));
        Assert.StartsWith("definition changed", runtime.ReasonOf(Fulfillment, "A-1"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task a_changed_definition_can_not_be_resumed_but_the_original_one_can()
    {
        var runtime = TestSystem.Create(Fulfillment);
        await runtime.SendAsync(new PlaceOrder("A-1", Quantity: 1, Amount: 50));
        runtime.Redeploy(Reshaped);
        await runtime.PublishAsync(new PaymentConfirmed("A-1"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.ResumeAsync(Fulfillment, "A-1"));

        runtime.Redeploy(Fulfillment); // roll back
        await runtime.ResumeAsync(Fulfillment, "A-1");

        Assert.Equal(ProcessStatus.Completed, runtime.StatusOf(Fulfillment, "A-1")); // the parked confirmation was kept
    }

    [Fact]
    public async Task a_reply_to_an_instance_whose_definition_changed_is_not_lost()
    {
        var runtime = TestSystem.Create(Fulfillment);
        runtime.FailNext<ChargePayment>();
        await runtime.SendAsync(new PlaceOrder("A-1", Quantity: 1, Amount: 50)); // the charge is retried in 1 s

        runtime.Redeploy(Reshaped);
        await runtime.AdvanceAsync(1.Seconds);
        Assert.Equal(ProcessStatus.Suspended, runtime.StatusOf(Fulfillment, "A-1"));

        runtime.Redeploy(Fulfillment);
        await runtime.ResumeAsync(Fulfillment, "A-1");
        await runtime.PublishAsync(new PaymentConfirmed("A-1"));

        Assert.Equal(ProcessStatus.Completed, runtime.StatusOf(Fulfillment, "A-1"));
        Assert.Single(runtime.History.OfType<PaymentCharged>());
    }

    [Fact]
    public async Task tuning_retries_or_time_limits_does_not_disturb_instances_in_flight()
    {
        var runtime = TestSystem.Create(Fulfillment);
        await runtime.SendAsync(new PlaceOrder("A-1", Quantity: 1, Amount: 50));

        runtime.Redeploy(Process.StartOn<OrderPlaced>(e => e.OrderId, Fulfillment.Name)
            .Then(Stock.Reserve).Retry(5.Times).Undo(Stock.Release)
            .Then(Payment.Charge).Retry(5.Times).Undo(Payment.Refund)
            .Await<PaymentConfirmed>(e => e.OrderId).Within(1.Hours)
            .Then(Shipping.Ship).Retry(5.Times));
        await runtime.PublishAsync(new PaymentConfirmed("A-1"));

        Assert.Equal(ProcessStatus.Completed, runtime.StatusOf(Fulfillment, "A-1"));
    }
}
