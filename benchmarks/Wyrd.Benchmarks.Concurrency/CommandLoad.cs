using System.Diagnostics;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Shop.Inventory;
using Shop.Orders;

namespace Wyrd.Benchmarks.Concurrency;

/// <summary>
/// What an API sees: many requests at once, each one <c>SendAsync</c> on the one runtime of the process (a singleton),
/// each in a transaction of its own. Spread: every command is for a different order. Hot: every command is for the
/// same SKU, so every transaction writes the same row.
/// </summary>
internal static class CommandLoad
{
    public static async Task RunAsync(Server server, int commands, int[] concurrency)
    {
        Console.WriteLine($"## Commands: {commands:N0} SendAsync calls per run, from N concurrent callers");
        Console.WriteLine();
        Console.WriteLine("| Keys | Callers | Commands/s | p50 ms | p95 ms | p99 ms | Conflicts retried | Failed: typed | Failed: untyped |");
        Console.WriteLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
        foreach (var hot in new[] { false, true })
        {
            foreach (var callers in concurrency)
            {
                var contexts = await server.NewDatabaseAsync();
                var conflicts = new Conflicts();
                using var shop = Server.Shop(contexts, conflicts);
                Send send = hot
                    ? i => shop.SendAsync(new Restock("HOT", 1))
                    : i => shop.SendAsync(new PlaceOrder("O-" + i.ToString(CultureInfo.InvariantCulture), "SKU", 1, 10m));
                await LoadAsync(Math.Min(100, commands), callers, send); // warm up the pool and the JIT
                var warm = conflicts.Retried;
                var run = await LoadAsync(commands, callers, i => send(i + commands));
                Console.WriteLine($"| {(hot ? "hot (one SKU)" : "spread")} | {callers} | {commands / run.Elapsed.TotalSeconds:F0} | "
                    + $"{run.Percentile(0.50):F1} | {run.Percentile(0.95):F1} | {run.Percentile(0.99):F1} | {conflicts.Retried - warm:N0} | {run.Failed:N0} | {run.Untyped:N0} |");
            }
        }
    }

    private delegate Task<Decision> Send(int index);

    /// <summary><paramref name="commands"/> calls, <paramref name="callers"/> at a time: each caller sends the next one as soon as its last returned.</summary>
    private static async Task<Run> LoadAsync(int commands, int callers, Send send)
    {
        var latencies = new double[commands];
        var next = -1;
        var failed = 0;
        var untyped = 0;
        var clock = Stopwatch.StartNew();
        await Task.WhenAll(Enumerable.Range(0, callers).Select(_ => Task.Run(async () =>
        {
            int index;
            while ((index = Interlocked.Increment(ref next)) < commands)
            {
                var started = Stopwatch.GetTimestamp();
                try
                {
                    if (await send(index) is not Decision.Accepted)
                    {
                        throw new InvalidOperationException($"Command {index} was not accepted.");
                    }
                }
                catch (ConcurrencyConflictException)
                {
                    // Still conflicting after MaxConflictRetries: what a caller sees on a key too hot for its retries.
                    Interlocked.Increment(ref failed);
                }
                catch (DbUpdateException)
                {
                    // The same, but untyped: a unique key raced, and the provider's exception reached the caller.
                    Interlocked.Increment(ref untyped);
                }

                latencies[index] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            }
        })));
        clock.Stop();
        Array.Sort(latencies);
        return new Run(clock.Elapsed, latencies, failed, untyped);
    }

    private sealed record Run(TimeSpan Elapsed, double[] Latencies, int Failed, int Untyped)
    {
        public double Percentile(double p) => Latencies[Math.Min(Latencies.Length - 1, (int)Math.Ceiling(p * Latencies.Length) - 1)];
    }
}
