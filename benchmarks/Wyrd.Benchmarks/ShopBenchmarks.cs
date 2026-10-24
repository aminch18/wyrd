using System.Globalization;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

namespace Wyrd.Benchmarks;

[MemoryDiagnoser]
[Config(typeof(ShopBenchmarkConfig))]
public class ShopBenchmarks
{
    internal const int BatchSize = 1_000;

    internal static Dictionary<ShopScenario, MessageShape> MessageCounts { get; } = [];

    [ParamsAllValues]
    public ShopScenario Scenario { get; set; }

    [Benchmark(OperationsPerInvoke = BatchSize)]
    public Task ProcessOrders() => ShopScenarios.RunAsync(Scenario, BatchSize);

    internal static async Task CalibrateAsync()
    {
        foreach (var scenario in Enum.GetValues<ShopScenario>())
        {
            MessageCounts[scenario] = await ShopScenarios.CountMessagesAsync(scenario).ConfigureAwait(false);
        }
    }
}

public sealed class ShopBenchmarkConfig : ManualConfig
{
    public ShopBenchmarkConfig() => AddColumn(
        new WorkloadColumn("Orders/s"), new WorkloadColumn("Messages/s"), new WorkloadColumn("B/message"));
}

internal sealed class WorkloadColumn(string name) : IColumn
{
    public string Id => name;
    public string ColumnName => name;
    public bool AlwaysShow => true;
    public ColumnCategory Category => ColumnCategory.Custom;
    public int PriorityInCategory => 0;
    public bool IsNumeric => true;
    public UnitType UnitType => UnitType.Dimensionless;
    public string Legend => name == "B/message"
        ? "Bytes per handled envelope, from MemoryDiagnoser's bytes/order and a separate tracing probe."
        : name == "Messages/s" ? "Handled envelopes per second; tracing is disabled during measurement." : "Orders completed or compensated per second.";

    public bool IsAvailable(Summary summary) => true;

    public bool IsDefault(Summary summary, BenchmarkCase benchmarkCase) => false;

    public string GetValue(Summary summary, BenchmarkCase benchmarkCase) => GetValue(summary, benchmarkCase, summary.Style);

    public string GetValue(Summary summary, BenchmarkCase benchmarkCase, SummaryStyle style)
    {
        var report = summary[benchmarkCase];
        if (report?.ResultStatistics is not { Mean: > 0 } statistics)
        {
            return "NA";
        }

        var scenario = (ShopScenario)benchmarkCase.Parameters.Items.Single().Value;
        var messagesPerOrder = ShopBenchmarks.MessageCounts[scenario].Total(ShopBenchmarks.BatchSize) / ShopBenchmarks.BatchSize;
        var ordersPerSecond = 1e9 / statistics.Mean;
        var value = name switch
        {
            "Orders/s" => ordersPerSecond,
            "Messages/s" => ordersPerSecond * messagesPerOrder,
            _ => report.GcStats.GetBytesAllocatedPerOperation(benchmarkCase) / messagesPerOrder,
        };
        return value?.ToString("F2", CultureInfo.InvariantCulture) ?? "NA";
    }
}
