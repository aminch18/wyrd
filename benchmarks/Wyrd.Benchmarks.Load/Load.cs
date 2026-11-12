using System.Diagnostics;
using Shop.Contracts;
using Shop.Orders;
using Wyrd.Runtime;
using ShopProcesses = Shop.Fulfillment.Processes;

namespace Wyrd.Benchmarks.Load;

/// <summary>
/// The API of the Shop under a steady rate of orders: each order is placed (<c>SendAsync</c>) and its payment confirmed
/// (<c>PublishAsync</c>), as a client and a payment provider would. Every <c>--sample</c>th measured order is followed
/// to the end: its latency runs from when it was due until its fulfillment process completed, which the workers (other
/// containers) bring about. Following it is a lookup by key, never a count over a table under load, and the lookups
/// slow down as the order waits (every 20 ms, or a twentieth of the wait so far): a backlog must not fill the database
/// with the benchmark's own queries. A latency is then at most about 5% late.
/// </summary>
internal static class Load
{
    public static async Task<int> RunAsync(string connection, Options options)
    {
        var rate = options.Decimal("rate", 100);
        var warmup = TimeSpan.FromSeconds(options.Number("warmup", 30));
        var duration = TimeSpan.FromSeconds(options.Number("duration", 120));
        var skus = options.Number("skus", 1_000);
        var sample = options.Number("sample", 10);
        var settle = TimeSpan.FromSeconds(options.Number("settle", 60));
        var label = options.Text("label") ?? "";
        var loop = new OpenLoop(rate, warmup, duration, options.Number("max-in-flight", 5_000));

        using var shop = ShopHost.Runtime(ShopHost.Contexts(connection, options.Number("pool", 100)));
        var placed = new Latencies();
        var finished = new Latencies();
        long conflicts = 0, errors = 0, unfinished = 0;
        var started = Stopwatch.GetTimestamp();
        var stopFollowing = started + (long)((warmup + duration + settle).TotalSeconds * Stopwatch.Frequency);
        Console.WriteLine($"Load: {rate} orders/s on {skus:N0} SKU(s), {warmup.TotalSeconds:F0} s warm-up, {duration.TotalSeconds:F0} s measured.");

        var sent = await loop.RunAsync(
            async (index, due, measured) =>
            {
                var id = "O-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
                try
                {
                    if (await shop.SendAsync(new PlaceOrder(id, ShopHost.Sku((int)(index % skus)), 1, 10m)) is not Decision.Accepted and var decision)
                    {
                        throw new InvalidOperationException($"{id} was not accepted: {decision}.");
                    }

                    await shop.PublishAsync(new PaymentConfirmed(id));
                }
                catch (ConcurrencyConflictException)
                {
                    Interlocked.Increment(ref conflicts); // a key too hot for SendAsync's retries
                    return;
                }
                catch (Exception ex)
                {
                    if (Interlocked.Increment(ref errors) == 1)
                    {
                        Console.Error.WriteLine($"First failure: {ex}");
                    }

                    return;
                }

                if (!measured)
                {
                    return;
                }

                placed.Add(due);
                if (index % sample == 0)
                {
                    while (await shop.StatusOfAsync(ShopProcesses.OrderFulfillment, id) != ProcessStatus.Completed)
                    {
                        if (Stopwatch.GetTimestamp() > stopFollowing)
                        {
                            Interlocked.Increment(ref unfinished);
                            return;
                        }

                        var waited = Stopwatch.GetElapsedTime(due);
                        await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(20, waited.TotalMilliseconds / 20)));
                    }

                    finished.Add(due);
                }
            },
            elapsed => Console.WriteLine($"{elapsed.TotalSeconds,6:F0} s  in flight {loop.InFlight,6}  dropped {loop.Dropped}  conflicts {Interlocked.Read(ref conflicts)}  errors {Interlocked.Read(ref errors)}  done {finished.TakeWindow()}"));

        var measuredSeconds = duration.TotalSeconds;
        Console.WriteLine();
        Console.WriteLine("| Run | Orders/s asked | Orders/s placed | Placed p50 ms | p99 | Order done p50 ms | p95 | p99 | max | Followed | Not done | Conflicts | Errors | Dropped |");
        Console.WriteLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
        Console.WriteLine($"| {label} | {rate:F0} | {placed.Count / measuredSeconds:F0} | {placed.Percentile(0.50)} | {placed.Percentile(0.99)} | "
            + $"{finished.Percentile(0.50)} | {finished.Percentile(0.95)} | {finished.Percentile(0.99)} | {finished.Max} | {finished.Count:N0} | "
            + $"{unfinished:N0} | {conflicts:N0} | {errors:N0} | {loop.Dropped:N0} |");
        Console.WriteLine($"Sent {sent:N0} orders in {Stopwatch.GetElapsedTime(started).TotalSeconds:F0} s.");
        return errors > 0 || loop.Dropped > 0 ? 1 : 0;
    }
}
