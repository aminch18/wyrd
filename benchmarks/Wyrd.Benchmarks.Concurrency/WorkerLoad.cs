using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using Shop.Contracts;
using Shop.Inventory;
using Shop.Orders;
using Wyrd.EntityFrameworkCore;
using Wyrd.Runtime;
using ShopProcesses = Shop.Fulfillment.Processes;

namespace Wyrd.Benchmarks.Concurrency;

/// <summary>
/// What the workers do with a backlog: every order is placed and paid first, then N workers, each in a runtime of its
/// own (as N instances of an application would), handle everything that follows until every order is shipped. Spread:
/// one SKU per order. Hot: every order reserves the same SKU, so workers write the same stock row.
/// </summary>
internal static class WorkerLoad
{
    public static async Task RunAsync(Server server, int orders, int[] workers)
    {
        Console.WriteLine($"## Workers: {orders:N0} orders placed and paid, then handled to the end by N workers");
        Console.WriteLine();
        Console.WriteLine("| Keys | Workers | Orders/s | Messages/s | Messages | Conflicts retried |");
        Console.WriteLine("| --- | ---: | ---: | ---: | ---: | ---: |");
        foreach (var hot in new[] { false, true })
        {
            foreach (var count in workers)
            {
                var contexts = await server.NewDatabaseAsync();
                var conflicts = new Conflicts();
                using var shop = Server.Shop(contexts, conflicts);
                await PlaceAsync(shop, orders, hot);

                var runtimes = Enumerable.Range(0, count).Select(_ => Server.Shop(contexts, conflicts)).ToList();
                using var stop = new CancellationTokenSource();
                using var handled = new Handled();
                var clock = Stopwatch.StartNew();
                var running = runtimes.Select(runtime => runtime.RunAsync(stop.Token)).ToList();
                while (await shop.HealthAsync() is { Due: > 0 } or { PendingSteps: > 0 })
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(50));
                }

                clock.Stop();
                await stop.CancelAsync();
                await Task.WhenAll(running);
                runtimes.ForEach(runtime => runtime.Dispose());
                await CheckAsync(shop, orders);
                Console.WriteLine($"| {(hot ? "hot (one SKU)" : "spread")} | {count} | {orders / clock.Elapsed.TotalSeconds:F0} | "
                    + $"{handled.Count / clock.Elapsed.TotalSeconds:F0} | {handled.Count:N0} | {conflicts.Retried:N0} |");
            }
        }
    }

    /// <summary>The backlog, before any worker runs: stock, then every order placed and its payment confirmed.</summary>
    private static async Task PlaceAsync(EfCoreRuntime<WyrdDbContext> shop, int orders, bool hot)
    {
        if (hot)
        {
            await shop.SendAsync(new Restock("HOT", orders));
        }

        for (var index = 0; index < orders; index++)
        {
            var id = "O-" + index.ToString(CultureInfo.InvariantCulture);
            var sku = hot ? "HOT" : "SKU-" + id;
            if (!hot)
            {
                await shop.SendAsync(new Restock(sku, 1));
            }

            await shop.SendAsync(new PlaceOrder(id, sku, 1, 10m));
            await shop.PublishAsync(new PaymentConfirmed(id));
        }
    }

    /// <summary>A broken run must not report cheap orders: every one reached the end.</summary>
    private static async Task CheckAsync(EfCoreRuntime<WyrdDbContext> shop, int orders)
    {
        for (var index = 0; index < orders; index++)
        {
            var id = "O-" + index.ToString(CultureInfo.InvariantCulture);
            if (await shop.StatusOfAsync(ShopProcesses.OrderFulfillment, id) != ProcessStatus.Completed)
            {
                throw new InvalidOperationException($"Order {id} did not complete.");
            }
        }
    }

    /// <summary>The messages workers handled, from the runtime's own <c>wyrd.messages.handled</c> counter.</summary>
    internal sealed class Handled : IDisposable
    {
        private readonly MeterListener _listener = new();
        private long _count;

        public Handled()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == "Wyrd" && instrument.Name == "wyrd.messages.handled")
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>((_, value, _, _) => Interlocked.Add(ref _count, value));
            _listener.Start();
        }

        public long Count => Interlocked.Read(ref _count);

        public void Dispose() => _listener.Dispose();
    }
}
