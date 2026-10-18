using Wyrd.Processes;
using Wyrd.Runtime;

namespace Wyrd.Tests;

public class HistoryTests
{
    private static readonly Process Fulfillment = FluentProcess.OrderFulfillment;

    private readonly InMemoryRuntime _runtime = TestSystem.Create(Fulfillment);

    private IEnumerable<string> Shape(string key) =>
        _runtime.HistoryOf(Fulfillment, key).Entries.Select(e => e.Kind switch
        {
            HistoryKind.Sent => $"Sent {Name(e.Type)} step {e.Step}{(e.Compensating ? " undo" : "")} attempt {e.Attempt}",
            HistoryKind.Replied => $"Replied {e.Outcome} {Name(e.Type)}",
            HistoryKind.Received or HistoryKind.Started => $"{e.Kind} {Name(e.Type)}",
            HistoryKind.Suspended => $"Suspended: {e.Reason}",
            _ => e.Kind.ToString(),
        }).Select(line => line.TrimEnd());

    private static string Name(string? type) => type?.Split('.', '+')[^1] ?? "";

    [Fact]
    public async Task an_instance_records_what_crossed_its_boundary()
    {
        await _runtime.SendAsync(new PlaceOrder("A-1", Quantity: 1, Amount: 50));
        await _runtime.PublishAsync(new PaymentConfirmed("A-1"));

        Assert.Equal(
            [
                "Started OrderPlaced",
                "Sent ReserveStock step 0 attempt 1",
                "Replied Accepted StockReserved",
                "Sent ChargePayment step 1 attempt 1",
                "Replied Accepted PaymentCharged",
                "Received payment-confirmed v1", // integration events are recorded by contract name and version
                "Sent ShipOrder step 3 attempt 1",
                "Replied Accepted OrderShipped",
                "Completed",
            ],
            Shape("A-1"));
    }

    [Fact]
    public async Task retries_rejections_and_compensation_are_recorded()
    {
        _runtime.FailNext<ChargePayment>();
        await _runtime.SendAsync(new PlaceOrder("A-1", Quantity: 1, Amount: 5_000)); // declined after one retry
        await _runtime.AdvanceAsync(1.Minutes);

        Assert.Equal(
            [
                "Started OrderPlaced",
                "Sent ReserveStock step 0 attempt 1",
                "Replied Accepted StockReserved",
                "Sent ChargePayment step 1 attempt 1",
                "Replied Faulted",
                "Sent ChargePayment step 1 attempt 2",
                "Replied Rejected PaymentError",
                "Sent ReleaseStock step 0 undo attempt 1",
                "Replied Accepted StockReleased",
                "Compensated",
            ],
            Shape("A-1"));
    }

    [Fact]
    public async Task suspension_and_operators_are_recorded()
    {
        _runtime.FailNext<ChargePayment>(times: 4);
        await _runtime.SendAsync(new PlaceOrder("A-1", Quantity: 1, Amount: 50));
        await _runtime.AdvanceAsync(1.Minutes);
        await _runtime.ResumeAsync(Fulfillment, "A-1");

        var shape = Shape("A-1").ToList();
        Assert.Contains("Suspended: Payment.Charge failed after 4 attempts", shape);
        Assert.Equal("Resumed", shape[shape.IndexOf("Suspended: Payment.Charge failed after 4 attempts") + 1]);
    }

    [Fact]
    public async Task a_timeout_is_recorded()
    {
        await _runtime.SendAsync(new PlaceOrder("A-1", Quantity: 1, Amount: 50));
        await _runtime.AdvanceAsync(15.Minutes);

        Assert.Contains("TimedOut", Shape("A-1"));
    }

    [Fact]
    public async Task a_history_round_trips_through_json()
    {
        await _runtime.SendAsync(new PlaceOrder("A-1", Quantity: 1, Amount: 50));
        var history = _runtime.HistoryOf(Fulfillment, "A-1");

        var copy = InstanceHistory.FromJson(history.ToJson());

        Assert.Equal(history.Process, copy.Process);
        Assert.Equal(history.Fingerprint, copy.Fingerprint);
        Assert.Equal(history.Entries, copy.Entries);
        Assert.Contains("\"Kind\": \"Sent\"", history.ToJson(), StringComparison.Ordinal);
    }
}
