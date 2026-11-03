using System.Collections.Concurrent;
using System.Diagnostics;

namespace Wyrd.Benchmarks.Load;

/// <summary>
/// Starts one piece of work every 1/<c>rate</c> seconds, whether the last ones finished or not: the load doesn't slow
/// down when the system does, so a queue building up shows in the latencies instead of hiding (coordinated omission).
/// Each latency counts from when its work was due, not from when it started. Work due before <c>warmup</c> runs but
/// isn't measured. Past <c>maxInFlight</c> unfinished, work is dropped and counted: the generator can't keep up, and the
/// run says so.
/// </summary>
internal sealed class OpenLoop(double rate, TimeSpan warmup, TimeSpan duration, int maxInFlight)
{
    private int _inFlight;
    private long _dropped;

    public long Dropped => Interlocked.Read(ref _dropped);

    public int InFlight => Volatile.Read(ref _inFlight);

    /// <summary>Runs <paramref name="work"/>(index, due, measured) at the rate, and waits for every one to finish.</summary>
    public async Task<long> RunAsync(Func<long, long, bool, Task> work, Action<TimeSpan>? progress = null)
    {
        var all = new ConcurrentBag<Task>();
        var start = Stopwatch.GetTimestamp();
        var total = (long)((warmup + duration).TotalSeconds * Stopwatch.Frequency);
        var nextReport = TimeSpan.FromSeconds(10);
        long index = 0;
        for (; ; index++)
        {
            var offset = (long)(index * Stopwatch.Frequency / rate);
            if (offset >= total)
            {
                break;
            }

            var due = start + offset;
            var wait = Stopwatch.GetElapsedTime(Stopwatch.GetTimestamp(), due);
            if (wait > TimeSpan.FromMilliseconds(1))
            {
                await Task.Delay(wait);
            }

            if (progress is not null && Stopwatch.GetElapsedTime(start) >= nextReport)
            {
                progress(nextReport);
                nextReport += TimeSpan.FromSeconds(10);
            }

            if (Interlocked.Increment(ref _inFlight) > maxInFlight)
            {
                Interlocked.Decrement(ref _inFlight);
                Interlocked.Increment(ref _dropped);
                continue;
            }

            var measured = offset >= (long)(warmup.TotalSeconds * Stopwatch.Frequency);
            var current = index; // the loop moves on before the task reads it
            all.Add(Task.Run(async () =>
            {
                try
                {
                    await work(current, due, measured);
                }
                finally
                {
                    Interlocked.Decrement(ref _inFlight);
                }
            }));
        }

        await Task.WhenAll(all);
        return index;
    }
}

/// <summary>Latencies in milliseconds, safe to add from many tasks; percentiles once the run is over.</summary>
internal sealed class Latencies
{
    private readonly ConcurrentQueue<double> _values = new();
    private double[]? _sorted;

    public int Count => _values.Count;

    public void Add(long due) => _values.Enqueue(Stopwatch.GetElapsedTime(due).TotalMilliseconds);

    public string Percentile(double p)
    {
        _sorted ??= [.. _values.Order()];
        return _sorted.Length == 0 ? "–" : _sorted[Math.Clamp((int)Math.Ceiling(p * _sorted.Length) - 1, 0, _sorted.Length - 1)].ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
    }

    public string Max => Percentile(1.0);
}
