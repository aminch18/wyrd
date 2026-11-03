using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Runtime.InteropServices;

namespace Wyrd.Benchmarks.Load;

/// <summary>
/// One instance of the application: one runtime, <c>--loops</c> worker loops, until stopped (SIGTERM, Ctrl+C). Every
/// <c>--report</c> seconds it prints the messages it handled, from the runtime's own <c>wyrd.messages.handled</c>.
/// </summary>
internal static class Worker
{
    public static async Task<int> RunAsync(string connection, Options options)
    {
        var loops = options.Number("loops", 1);
        var report = TimeSpan.FromSeconds(options.Number("report", 10));
        using var stop = new CancellationTokenSource();
        using var term = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
        {
            context.Cancel = true;
            stop.Cancel();
        });
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            stop.Cancel();
        };

        using var shop = ShopHost.Runtime(ShopHost.Contexts(connection));
        using var handled = new Handled();
        var running = Enumerable.Range(0, loops).Select(_ => shop.RunAsync(stop.Token)).ToList();
        Console.WriteLine($"Worker: {loops} loop(s).");

        var clock = Stopwatch.StartNew();
        long last = 0;
        try
        {
            while (!stop.IsCancellationRequested)
            {
                await Task.Delay(report, stop.Token);
                var now = handled.Count;
                Console.WriteLine($"{clock.Elapsed.TotalSeconds,6:F0} s  {(now - last) / report.TotalSeconds,8:F0} messages/s  {now,10:N0} in all");
                last = now;
            }
        }
        catch (OperationCanceledException)
        {
            // Stopped.
        }

        await Task.WhenAll(running);
        Console.WriteLine($"Worker stopped: {handled.Count:N0} messages in {clock.Elapsed.TotalSeconds:F0} s.");
        return 0;
    }

    /// <summary>The messages this process's workers handled, from the runtime's own counter.</summary>
    private sealed class Handled : IDisposable
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
