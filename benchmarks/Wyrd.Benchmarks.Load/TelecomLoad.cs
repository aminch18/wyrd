using System.Diagnostics;
using System.Globalization;
using Telecom;
using Telecom.Features.Subscriptions;
using Wyrd.Runtime;

namespace Wyrd.Benchmarks.Load;

/// <summary>
/// Customers contracting a subscription at a steady rate (<c>ContractSubscription</c>, as the API does). Every
/// <c>--sample</c>th measured one is followed until it is provisioned: its payment captured, its port submitted and its
/// eSIM provisioned, three provider calls made by the workers. The port's later webhooks are not sent: the run measures
/// how the workers get through slow third parties, not the operator's timing.
/// </summary>
internal static class TelecomLoad
{
    public static async Task<int> RunAsync(string connection, Options options)
    {
        var rate = options.Decimal("rate", 10);
        var warmup = TimeSpan.FromSeconds(options.Number("warmup", 30));
        var duration = TimeSpan.FromSeconds(options.Number("duration", 120));
        var sample = options.Number("sample", 10);
        var settle = TimeSpan.FromSeconds(options.Number("settle", 60));
        var label = options.Text("label") ?? "";
        var loop = new OpenLoop(rate, warmup, duration, options.Number("max-in-flight", 5_000));
        var runId = options.Text("run") ?? DateTime.UtcNow.ToString("HHmmss", CultureInfo.InvariantCulture);

        // The load only sends: no worker runs here, so the providers are never called from this process.
        using var telecom = TelecomHost.Runtime(TelecomHost.Contexts(connection, options.Number("pool", 100)), TimeSpan.Zero);
        var contracted = new Latencies();
        var provisioned = new Latencies();
        long conflicts = 0, errors = 0, unfinished = 0;
        var started = Stopwatch.GetTimestamp();
        var stopFollowing = started + (long)((warmup + duration + settle).TotalSeconds * Stopwatch.Frequency);
        Console.WriteLine($"Load: {rate} contracts/s, {warmup.TotalSeconds:F0} s warm-up, {duration.TotalSeconds:F0} s measured.");

        await loop.RunAsync(
            async (index, due, measured) =>
            {
                var id = $"sub-{runId}-{index}";
                var offer = new Offer(id, "cust-" + id, "+34" + (600_000_000 + index).ToString(CultureInfo.InvariantCulture), 1, id + "-profile", 25m);
                try
                {
                    if (await telecom.SendAsync(new ContractSubscription(offer)) is not Decision.Accepted and var decision)
                    {
                        throw new InvalidOperationException($"{id} was not accepted: {decision}.");
                    }
                }
                catch (ConcurrencyConflictException)
                {
                    Interlocked.Increment(ref conflicts);
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

                contracted.Add(due);
                if (index % sample == 0)
                {
                    while (await telecom.ViewAsync<CustomerProgress>(id) is not { Provisioned: true })
                    {
                        if (Stopwatch.GetTimestamp() > stopFollowing)
                        {
                            Interlocked.Increment(ref unfinished);
                            return;
                        }

                        await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(20, Stopwatch.GetElapsedTime(due).TotalMilliseconds / 20)));
                    }

                    provisioned.Add(due);
                }
            },
            elapsed => Console.WriteLine($"{elapsed.TotalSeconds,6:F0} s  in flight {loop.InFlight,6}  dropped {loop.Dropped}  conflicts {Interlocked.Read(ref conflicts)}  errors {Interlocked.Read(ref errors)}"));

        Console.WriteLine();
        Console.WriteLine("| Run | Contracts/s asked | Contracts/s accepted | Contract p50 ms | p99 | Provisioned p50 ms | p95 | p99 | max | Followed | Not provisioned | Conflicts | Errors | Dropped |");
        Console.WriteLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
        Console.WriteLine($"| {label} | {rate:F0} | {contracted.Count / duration.TotalSeconds:F1} | {contracted.Percentile(0.50)} | {contracted.Percentile(0.99)} | "
            + $"{provisioned.Percentile(0.50)} | {provisioned.Percentile(0.95)} | {provisioned.Percentile(0.99)} | {provisioned.Max} | {provisioned.Count:N0} | "
            + $"{unfinished:N0} | {conflicts:N0} | {errors:N0} | {loop.Dropped:N0} |");
        return errors > 0 || loop.Dropped > 0 ? 1 : 0;
    }
}
