using Spectre.Console.Cli.Testing;
using Spectre.Console.Testing;
using Wyrd.Runtime;

namespace Wyrd.Tool.Tests;

public sealed class ReplayTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.history.json");

    public ReplayTests() => File.WriteAllText(_path, new InstanceHistory("OrderFulfillment", "A-1", "d7c5dc7f958b",
    [
        new HistoryEntry(Start, HistoryKind.Started) { Type = "Shop.Orders.OrderPlaced", Payload = """{"OrderId":"A-1","Note":"say \"hi\""}""" },
        new HistoryEntry(Start, HistoryKind.Sent) { Type = "Shop.Payments.ChargePayment", Payload = "{}", Step = 1, Attempt = 1 },
        new HistoryEntry(Start, HistoryKind.Replied) { Outcome = "Faulted", Step = 1, Attempt = 1 },
        new HistoryEntry(Start.AddSeconds(1), HistoryKind.Sent) { Type = "Shop.Payments.ChargePayment", Payload = "{}", Step = 1, Attempt = 2 },
        new HistoryEntry(Start.AddSeconds(1), HistoryKind.Suspended) { Reason = "Payments.Charge failed after 2 attempts" },
        new HistoryEntry(Start.AddMinutes(5), HistoryKind.Received) { Type = "payments.payment-confirmed v1", Payload = "{}", Step = 2 },
    ]).ToJson());

    public void Dispose() => File.Delete(_path);

    [Fact]
    public void without_as_test_it_prints_the_timeline()
    {
        var (app, errors) = CreateApp();

        var result = app.Run("replay", _path);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(
            """
            OrderFulfillment 'A-1' (structure d7c5dc7f958b)
            [00:00:00] started by OrderPlaced
            [00:00:00] -> ChargePayment
            [00:00:00] faulted
            [00:00:01] -> ChargePayment attempt 2
            [00:00:01] SUSPENDED: Payments.Charge failed after 2 attempts
            [00:05:00] <- payments.payment-confirmed v1

            """.ReplaceLineEndings(),
            app.Console.Output.ReplaceLineEndings());
        Assert.Empty(errors.Output);
    }

    [Fact]
    public void as_test_writes_an_xunit_test_that_replays_the_embedded_history()
    {
        var (app, _) = CreateApp();

        var result = app.Run("replay", _path, "--as-test", "--runtime", "ShopModule.CreateRuntime",
            "--process", "Processes.OrderFulfillment", "--using", "Shop", "--namespace", "Shop.Tests");

        Assert.Equal(0, result.ExitCode);
        var test = app.Console.Output;
        Assert.Contains("using Shop;", test, StringComparison.Ordinal);
        Assert.Contains("namespace Shop.Tests;", test, StringComparison.Ordinal);
        Assert.Contains("public class OrderFulfillmentA1Replay", test, StringComparison.Ordinal);
        Assert.Contains("public async Task order_fulfillment_a_1_replays_as_recorded() =>", test, StringComparison.Ordinal);
        Assert.Contains(
            "(await Urd.ReplayAsync(ShopModule.CreateRuntime, Processes.OrderFulfillment, InstanceHistory.FromJson(History))).ThrowIfDiverged();",
            test, StringComparison.Ordinal);
    }

    [Fact]
    public void the_embedded_history_is_a_raw_string_long_enough_for_its_quotes()
    {
        var (app, _) = CreateApp();

        app.Run("replay", _path, "--as-test", "--runtime", "R", "--process", "P");

        // The payload contains \"hi\" — escaped quotes in JSON — so the delimiter needs more quotes than any run inside.
        var test = app.Console.Output;
        var json = File.ReadAllText(_path);
        var longest = json.Split('\\').Select(part => part.TakeWhile(c => c == '"').Count()).Max();
        Assert.Contains($"History = {new string('"', Math.Max(3, longest + 1))}", test, StringComparison.Ordinal);
        Assert.Equal(json.ReplaceLineEndings("\n").Trim(), Embedded(test));
    }

    [Fact]
    public void as_test_needs_the_runtime_and_the_process()
    {
        var (app, errors) = CreateApp();

        var result = app.Run("replay", _path, "--as-test");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("--as-test needs --runtime and --process", errors.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void a_missing_history_is_reported()
    {
        var (app, errors) = CreateApp();

        var result = app.Run("replay", _path + ".missing");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("History not found", errors.Output, StringComparison.Ordinal);
    }

    /// <summary>The raw string literal's content, with its indentation removed (as the compiler does).</summary>
    private static string Embedded(string test)
    {
        var lines = test.ReplaceLineEndings("\n").Split('\n');
        var open = Array.FindIndex(lines, l => l.Contains("History = \"\"\"", StringComparison.Ordinal));
        var close = Array.FindIndex(lines, open + 1, l => l.TrimStart().StartsWith("\"\"\"", StringComparison.Ordinal));
        return string.Join("\n", lines[(open + 1)..close].Select(l => l.Length >= 8 ? l[8..] : l.TrimStart()));
    }

    private static (CommandAppTester App, TestConsole Errors) CreateApp()
    {
        var errors = new TestConsole();
        var app = new CommandAppTester();
        app.Configure(config => Program.Configure(config, errors));
        return (app, errors);
    }
}
