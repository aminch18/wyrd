using Wyrd.Processes;
using Wyrd.Runtime;
using Wyrd.Testing;

namespace Wyrd.Tests
{
    public class UrdTests
    {
        private static readonly Process Fulfillment = FluentProcess.OrderFulfillment;

        // Today's code with a bug: same structure (same step names), but the charge doubles the amount.
        private static Process WithDoubleCharge => Process
            .StartOn<OrderPlaced>(e => e.OrderId, Fulfillment.Name)
            .Then(Stock.Reserve).Undo(Stock.Release)
            .Then(Changed.Payment.Charge).Retry(3.Times, Backoff.Exponential).Undo(Payment.Refund)
            .Await<PaymentConfirmed>(e => e.OrderId).Within(15.Minutes)
            .Then(Shipping.Ship);

        private static async Task<InstanceHistory> RecordAsync(Func<InMemoryRuntime, Task> scenario)
        {
            var runtime = TestSystem.Create(Fulfillment);
            await scenario(runtime);
            return InstanceHistory.FromJson(runtime.HistoryOf(Fulfillment, "A-1").ToJson()); // as if exported to a file
        }

        [Fact]
        public async Task a_completed_instance_replays_as_recorded()
        {
            var history = await RecordAsync(async shop =>
            {
                await shop.SendAsync(new PlaceOrder("A-1", Quantity: 1, Amount: 50));
                await shop.AdvanceAsync(3.Minutes);
                await shop.PublishAsync(new PaymentConfirmed("A-1"));
            });

            var report = await Urd.ReplayAsync(() => TestSystem.Create(Fulfillment), Fulfillment, history);

            report.ThrowIfDiverged();
            Assert.Equal(history.Entries.Count, report.Replayed.Count);
        }

        [Fact]
        public async Task retries_rejections_and_compensation_replay_as_recorded()
        {
            var history = await RecordAsync(async shop =>
            {
                shop.FailNext<ChargePayment>();
                await shop.SendAsync(new PlaceOrder("A-1", Quantity: 1, Amount: 5_000)); // declined after one retry
                await shop.AdvanceAsync(1.Minutes);
            });

            (await Urd.ReplayAsync(() => TestSystem.Create(Fulfillment), Fulfillment, history)).ThrowIfDiverged();
        }

        [Fact]
        public async Task a_timeout_replays_as_recorded()
        {
            var history = await RecordAsync(async shop =>
            {
                await shop.SendAsync(new PlaceOrder("A-1", Quantity: 1, Amount: 50));
                await shop.AdvanceAsync(1.Hours);
            });

            (await Urd.ReplayAsync(() => TestSystem.Create(Fulfillment), Fulfillment, history)).ThrowIfDiverged();
        }

        [Fact]
        public async Task suspension_and_an_operator_resume_replay_as_recorded()
        {
            var history = await RecordAsync(async shop =>
            {
                shop.FailNext<ChargePayment>(times: 4);
                await shop.SendAsync(new PlaceOrder("A-1", Quantity: 1, Amount: 50));
                await shop.AdvanceAsync(1.Minutes);
                await shop.ResumeAsync(Fulfillment, "A-1");
                await shop.PublishAsync(new PaymentConfirmed("A-1"));
            });

            Assert.Contains(history.Entries, e => e.Kind == HistoryKind.Resumed);
            (await Urd.ReplayAsync(() => TestSystem.Create(Fulfillment), Fulfillment, history)).ThrowIfDiverged();
        }

        [Fact]
        public async Task a_changed_step_function_is_the_first_difference()
        {
            var history = await RecordAsync(shop => shop.SendAsync(new PlaceOrder("A-1", Quantity: 1, Amount: 50)));

            var report = await Urd.ReplayAsync(() => TestSystem.Create(WithDoubleCharge), WithDoubleCharge, history);

            Assert.True(report.Diverged);
            Assert.Equal(HistoryKind.Sent, report.Divergence!.Recorded!.Kind);
            Assert.Contains("\"Amount\":50", report.Divergence.Recorded.Payload, StringComparison.Ordinal);
            Assert.Contains("\"Amount\":100", report.Divergence.Replayed!.Payload, StringComparison.Ordinal);
            var ex = Assert.Throws<WyrdAssertionException>(report.ThrowIfDiverged);
            Assert.Contains("First difference at entry 3:", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task a_changed_structure_is_reported_before_replaying()
        {
            var history = await RecordAsync(shop => shop.SendAsync(new PlaceOrder("A-1", Quantity: 1, Amount: 50)));
            Process reshaped = Process.StartOn<OrderPlaced>(e => e.OrderId, Fulfillment.Name)
                .Then(Stock.Reserve).Undo(Stock.Release)
                .Then(Shipping.Ship);

            var report = await Urd.ReplayAsync(() => TestSystem.Create(reshaped), reshaped, history);

            Assert.True(report.Diverged);
            Assert.Contains("structure of OrderFulfillment changed", report.Reason, StringComparison.Ordinal);
        }

        [Fact]
        public async Task a_history_of_another_process_is_refused()
        {
            var history = await RecordAsync(shop => shop.SendAsync(new PlaceOrder("A-1", Quantity: 1, Amount: 50)));
            Process other = Process.StartOn<OrderPlaced>(e => e.OrderId, "Other").Then(Shipping.Ship);

            await Assert.ThrowsAsync<ArgumentException>(() => Urd.ReplayAsync(() => TestSystem.Create(other), other, history));
        }

        [Fact]
        public async Task the_report_of_a_faithful_replay_says_so()
        {
            var history = await RecordAsync(shop => shop.SendAsync(new PlaceOrder("A-1", Quantity: 1, Amount: 50)));

            var report = await Urd.ReplayAsync(() => TestSystem.Create(Fulfillment), Fulfillment, history);

            Assert.False(report.Diverged);
            Assert.Equal($"Urd replayed OrderFulfillment 'A-1': {history.Entries.Count} entries, as recorded.", report.ToString());
        }
    }
}

namespace Wyrd.Tests.Changed
{
    public static class Payment
    {
        public static ChargePayment Charge(OrderPlaced e) => new(e.OrderId, e.Amount * 2);
    }
}
