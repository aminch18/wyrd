using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace Wyrd.Benchmarks;

internal static class AllocationGate
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private const int Small = 1_000;
    private const int Large = 10_000;
    private const double Tolerance = 1.10;

    public static async Task<int> RunAsync(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        var write = args.Contains("--write-baseline", StringComparer.Ordinal);
        var allowKnown = args.Contains("--known-single-sku-failure", StringComparer.Ordinal);
        if (args.Any(arg => arg is not ("--gate" or "--write-baseline" or "--known-single-sku-failure")) || write && allowKnown)
        {
            Console.Error.WriteLine("Use --gate [--known-single-sku-failure] or --write-baseline.");
            return 2;
        }

        var path = FindBaseline();
        Baseline? baseline = null;
        if (!write)
        {
            baseline = JsonSerializer.Deserialize<Baseline>(await File.ReadAllTextAsync(path).ConfigureAwait(false));
            if (baseline?.SchemaVersion != 1 || baseline.Orders != Large || baseline.Scenarios.Count != Enum.GetValues<ShopScenario>().Length)
            {
                throw new InvalidOperationException("The allocation baseline is incomplete or has an unsupported schema.");
            }
        }

        var measured = new Dictionary<string, double>(StringComparer.Ordinal);
        var failures = new List<string>();
        var clock = Stopwatch.StartNew();
        Console.WriteLine("Scenario | Orders | Envelopes | Orders/s | Messages/s | B/order | B/message | 10k/1k");
        foreach (var scenario in Enum.GetValues<ShopScenario>())
        {
            var shape = await ShopScenarios.CountMessagesAsync(scenario).ConfigureAwait(false);
            await ShopScenarios.RunAsync(scenario, 100).ConfigureAwait(false);
            var small = await MeasureAsync(scenario, Small, shape).ConfigureAwait(false);
            var large = await MeasureAsync(scenario, Large, shape).ConfigureAwait(false);
            var ratio = large.BytesPerOrder / small.BytesPerOrder;
            Print(small, null);
            Print(large, ratio);
            measured.Add(scenario.ToString(), large.BytesPerMessage);
            var known = allowKnown && scenario == ShopScenario.SingleSku;
            if (ratio > Tolerance)
            {
                var message = $"Q1.2 {scenario}: {large.BytesPerOrder:F2} B/order at {Large:N0} > "
                    + $"1.10 x {small.BytesPerOrder:F2} at {Small:N0} ({ratio:F3}x)";
                if (known)
                {
                    Console.WriteLine($"KNOWN FAILURE (task 016, PR #35): {message}");
                }
                else
                {
                    failures.Add(message);
                }
            }

            if (baseline is not null)
            {
                if (!baseline.Scenarios.TryGetValue(scenario.ToString(), out var budget) || !double.IsFinite(budget) || budget <= 0)
                {
                    throw new InvalidOperationException($"Missing or invalid budget for {scenario}.");
                }

                if (large.BytesPerMessage > budget * Tolerance)
                {
                    // The known task016 exception is scaling only; allocation budgets always fail closed.
                    failures.Add($"Q1.3 {scenario}: {large.BytesPerMessage:F2} B/message > "
                        + $"1.10 x baseline {budget:F2} ({budget * Tolerance:F2} B/message)");
                }
            }
        }

        if (write)
        {
            var value = new Baseline(1, Large, measured);
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, JsonOptions) + "\n").ConfigureAwait(false);
            Console.WriteLine($"Wrote {path} from this run; explain any baseline change in the PR.");
        }

        foreach (var failure in failures)
        {
            Console.Error.WriteLine(failure);
        }

        Console.WriteLine($"Gate completed in {clock.Elapsed.TotalSeconds:F2} s; {failures.Count} unexpected failure(s).");
        return failures.Count == 0 ? 0 : 1;
    }

    private static async Task<Measurement> MeasureAsync(ShopScenario scenario, int orders, MessageShape shape)
    {
        // Exclude previous workloads from live memory without subtracting allocation or setup costs from this run.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var allocated = GC.GetTotalAllocatedBytes(precise: true);
        var timer = Stopwatch.StartNew();
        await ShopScenarios.RunAsync(scenario, orders).ConfigureAwait(false);
        timer.Stop();
        var bytes = GC.GetTotalAllocatedBytes(precise: true) - allocated;
        return new Measurement(scenario, orders, shape.Total(orders), bytes, timer.Elapsed.TotalSeconds);
    }

    private static void Print(Measurement value, double? ratio) => Console.WriteLine(
        $"{value.Scenario} | {value.Orders} | {value.Messages:F0} | {value.Orders / value.Seconds:F1} | "
        + $"{value.Messages / value.Seconds:F1} | {value.BytesPerOrder:F2} | {value.BytesPerMessage:F2} | {ratio?.ToString("F3", CultureInfo.InvariantCulture) ?? "-"}");

    private static string FindBaseline()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var folder = Path.Combine(directory.FullName, "benchmarks");
            if (Directory.Exists(Path.Combine(folder, "Wyrd.Benchmarks")))
            {
                return Path.Combine(folder, "baseline.json");
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Run the benchmark from a Wyrd source checkout.");
    }

    private sealed record Baseline(int SchemaVersion, int Orders, Dictionary<string, double> Scenarios);

    private sealed record Measurement(ShopScenario Scenario, int Orders, double Messages, long Bytes, double Seconds)
    {
        public double BytesPerOrder => Bytes / (double)Orders;

        public double BytesPerMessage => Bytes / Messages;
    }
}
