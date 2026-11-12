using System.Diagnostics.CodeAnalysis;
using Wyrd.Runtime;

namespace Wyrd.Tests;

/// <summary>
/// Two workers handling two events of one stream at the same time, the later event first: one holds it, the other
/// projects the gap. Whichever commits second must fail and retry, or the held event waits forever.
/// </summary>
public class ConcurrentProjectionTests
{
    public sealed record OrderLog(string OrderId, int Events);

    private sealed class OrderLogs : Projection<OrderLog>
    {
        public OrderLogs()
        {
            On<OrderPlaced>(e => e.OrderId, (view, e) => new OrderLog(e.OrderId, (view?.Events ?? 0) + 1));
            On<PaymentCharged>(e => e.OrderId, (view, _) => view! with { Events = view.Events + 1 });
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [RequiresUnreferencedCode("Reflection-based JSON in a test.")]
    [RequiresDynamicCode("Reflection-based JSON in a test.")]
    public async Task an_event_held_while_another_worker_projects_the_gap_is_never_left_behind(bool holdingCommitsLast)
    {
        var application = new Application(MessageTypes.Reflection());
        application.Add(new PlaceOrderSlice());
        application.Add(new ChargePaymentSlice());
        application.Add(new OrderLogs());
        var store = new InMemoryStore(() => DateTimeOffset.UnixEpoch);
        var gated = new GatedStore(store);
        var ids = 0;
        Engine EngineOn(IWyrdStore on) => new(
            application.Registry, on, application.Types, () => DateTimeOffset.UnixEpoch, () => new Guid(++ids, 0, 0, new byte[8]), (_, _) => false, (_, _, _) => { });
        var direct = EngineOn(store);
        var paused = EngineOn(gated);

        await direct.HandleAsync(Command(application, new PlaceOrder("A-1", Quantity: 1, Amount: 50)), default);
        await direct.HandleAsync(Command(application, new ChargePayment("A-1", 50)), default);
        var events = await store.TakeOutboxAsync(default);
        var (first, second) = (events.Single(e => e.Stream!.Version == 1), events.Single(e => e.Stream!.Version == 2));
        var (last, other) = holdingCommitsLast ? (second, first) : (first, second);

        // Both workers read the stream before either commits; the one that commits last is paused at its commit.
        var lastCommit = paused.HandleAsync(last, default).AsTask();
        await gated.ReachedCommit;
        await direct.HandleAsync(other, default);
        gated.Open();

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => lastCommit);
        await direct.HandleAsync(last, default); // the retry, on what the other worker committed

        Assert.Equal(new OrderLog("A-1", 2), View(application, store, "A-1"));
    }

    /// <summary>The in-memory store, with its next commit paused until <see cref="Open"/>.</summary>
    private sealed class GatedStore(InMemoryStore inner) : IWyrdStore
    {
        private readonly TaskCompletionSource _reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _open = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task ReachedCommit => _reached.Task;

        public void Open() => _open.SetResult();

        public async ValueTask CommitAsync(Changes changes, CancellationToken cancellationToken)
        {
            _reached.SetResult();
            await _open.Task;
            await inner.CommitAsync(changes, cancellationToken);
        }

        public ValueTask<StoredState?> LoadStateAsync(string stateType, string key, CancellationToken cancellationToken) =>
            inner.LoadStateAsync(stateType, key, cancellationToken);

        public ValueTask<StoredInstance?> LoadInstanceAsync(string process, string key, CancellationToken cancellationToken) =>
            inner.LoadInstanceAsync(process, key, cancellationToken);

        public ValueTask<ReplyMessage?> FindReplyAsync(Guid commandId, CancellationToken cancellationToken) =>
            inner.FindReplyAsync(commandId, cancellationToken);

        public ValueTask<StoredParked?> FindParkedAsync(string process, string key, int step, CancellationToken cancellationToken) =>
            inner.FindParkedAsync(process, key, step, cancellationToken);

        public ValueTask<StoredView?> LoadViewAsync(string viewType, string key, CancellationToken cancellationToken) =>
            inner.LoadViewAsync(viewType, key, cancellationToken);

        public ValueTask<ProjectedStream?> LoadProjectedAsync(string stateType, string key, CancellationToken cancellationToken) =>
            inner.LoadProjectedAsync(stateType, key, cancellationToken);

        public ValueTask<StoredHeld?> FindHeldAsync(string stateType, string key, long version, CancellationToken cancellationToken) =>
            inner.FindHeldAsync(stateType, key, version, cancellationToken);

        public ValueTask<bool> WasHandledAsync(Guid messageId, CancellationToken cancellationToken) =>
            inner.WasHandledAsync(messageId, cancellationToken);

        public ValueTask<bool> IsCaughtUpAsync(string viewType, CancellationToken cancellationToken) =>
            inner.IsCaughtUpAsync(viewType, cancellationToken);

        public ValueTask<long?> LoadSeededAsync(StoredSeeded seeded, CancellationToken cancellationToken) =>
            inner.LoadSeededAsync(seeded, cancellationToken);

        public ValueTask<bool> SeedsAheadAsync(string viewType, CancellationToken cancellationToken) =>
            inner.SeedsAheadAsync(viewType, cancellationToken);

        public ValueTask<IReadOnlyList<StoredState>> UnseededAsync(string viewType, string stateType, int max, CancellationToken cancellationToken) =>
            inner.UnseededAsync(viewType, stateType, max, cancellationToken);
    }

    private static Envelope Command(Application application, ICommand command)
    {
        var (type, payload) = application.Types.Serialize(command, command.GetType());
        return new Envelope(Guid.NewGuid(), MessageKind.Command, type, payload);
    }

    private static OrderLog? View(Application application, InMemoryStore store, string key) =>
        store.View(typeof(OrderLog).FullName!, key) is { } view ? (OrderLog)application.Types.Deserialize(view.Type!, view.Payload!) : null;
}
