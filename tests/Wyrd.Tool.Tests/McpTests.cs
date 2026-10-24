using System.Collections.Concurrent;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Shop;
using Shop.Contracts;
using Shop.Inventory;
using Shop.Orders;
using Spectre.Console.Cli.Testing;
using Spectre.Console.Testing;
using Xunit.Abstractions;
using YamlDotNet.RepresentationModel;
using ShopProcesses = Shop.Fulfillment.Processes;

namespace Wyrd.Tool.Tests;

public sealed class McpTests(ITestOutputHelper output)
{
    private static readonly string Snapshot = Path.Combine(AppContext.BaseDirectory, "Samples", "contracts.wyrd.txt");
    private static readonly string ProcessSnapshot = Path.Combine(AppContext.BaseDirectory, "Samples", "processes.wyrd.txt");
    private static readonly string ShopAssembly = typeof(ShopModule).Assembly.Location;
    private static readonly string[] TestUsings = ["Shop", "Shop.Fulfillment"];

    [Fact]
    public async Task client_discovers_tools_and_parameter_descriptions()
    {
        await using var session = await McpTestSession.StartAsync();
        var tools = await session.Client.ListToolsAsync(cancellationToken: session.Token);

        Assert.Equal(["asyncapi", "contracts", "diagram", "processes", "replay"], tools.Select(tool => tool.Name).Order(StringComparer.Ordinal));
        foreach (var tool in tools)
        {
            Assert.False(string.IsNullOrWhiteSpace(tool.Description));
            Assert.True(tool.ProtocolTool.Annotations?.ReadOnlyHint);
            foreach (var property in tool.JsonSchema.GetProperty("properties").EnumerateObject())
            {
                Assert.False(string.IsNullOrWhiteSpace(property.Value.GetProperty("description").GetString()));
            }
        }

        Assert.NotNull(tools.Single(tool => tool.Name == "contracts").ProtocolTool.OutputSchema);
    }

    [Fact]
    public async Task diagram_and_asyncapi_are_the_same_documents_as_the_shop_cli()
    {
        await using var session = await McpTestSession.StartAsync();
        var diagram = await session.Client.CallToolAsync("diagram", new Dictionary<string, object?> { ["assemblyPath"] = ShopAssembly }, cancellationToken: session.Token);
        Assert.False(diagram.IsError is true);
        Assert.Equal(Cli("diagram", "--assembly", ShopAssembly), Text(diagram));

        var api = await session.Client.CallToolAsync("asyncapi", new Dictionary<string, object?>
        {
            ["contractsPath"] = Snapshot,
            ["title"] = "Shop API [v1]",
            ["version"] = "2.0",
        }, cancellationToken: session.Token);
        Assert.False(api.IsError is true);
        Assert.Equal(Cli("asyncapi", "--contracts", Snapshot, "--title", "Shop API [v1]", "--version", "2.0"), Text(api));

        var defaults = await session.Client.CallToolAsync("asyncapi", new Dictionary<string, object?> { ["contractsPath"] = Snapshot }, cancellationToken: session.Token);
        Assert.Equal(Cli("asyncapi", "--contracts", Snapshot), Text(defaults));
    }

    [Fact]
    public async Task structured_contracts_match_the_public_messages_in_the_cli_asyncapi()
    {
        await using var session = await McpTestSession.StartAsync();
        var result = await session.Client.CallToolAsync("contracts", new Dictionary<string, object?> { ["contractsPath"] = Snapshot }, cancellationToken: session.Token);
        Assert.False(result.IsError is true);
        var contracts = result.StructuredContent!.Value.GetProperty("contracts").EnumerateArray().ToArray();
        var yaml = new YamlStream();
        yaml.Load(new StringReader(Cli("asyncapi", "--contracts", Snapshot)));
        var document = (YamlMappingNode)yaml.Documents[0].RootNode;
        var messages = (YamlMappingNode)((YamlMappingNode)document.Children["components"]).Children["messages"];
        Assert.Equal(messages.Children.Count, contracts.Length);
        foreach (var contract in contracts)
        {
            var name = contract.GetProperty("name").GetString();
            var version = contract.GetProperty("version").GetInt32();
            var message = (YamlMappingNode)messages.Children[$"{name}.v{version}"];
            var payload = (YamlMappingNode)message.Children["payload"];
            var properties = (YamlMappingNode)payload.Children["properties"];
            Assert.Equal(contract.GetProperty("fields").GetArrayLength(), properties.Children.Count);
            foreach (var field in contract.GetProperty("fields").EnumerateArray())
            {
                var property = (YamlMappingNode)properties.Children[field.GetProperty("name").GetString()!];
                var type = field.GetProperty("type").GetString();
                Assert.Equal(type == "decimal" ? "number" : "string", ((YamlScalarNode)property.Children["type"]).Value);
            }

            Assert.False(contract.GetProperty("retired").GetBoolean());
        }
    }

    [Fact]
    public async Task processes_snapshot_and_check_are_the_same_output_as_the_shop_cli()
    {
        await using var session = await McpTestSession.StartAsync();
        var snapshot = await session.Client.CallToolAsync("processes", new Dictionary<string, object?> { ["assemblyPath"] = ShopAssembly }, cancellationToken: session.Token);
        Assert.False(snapshot.IsError is true);
        Assert.Equal(Cli("processes", "--assembly", ShopAssembly), Text(snapshot));
        Assert.Equal(File.ReadAllText(ProcessSnapshot).ReplaceLineEndings("\n"), Text(snapshot));

        var check = await session.Client.CallToolAsync("processes", new Dictionary<string, object?>
        {
            ["assemblyPath"] = ShopAssembly,
            ["snapshotPath"] = ProcessSnapshot,
        }, cancellationToken: session.Token);
        Assert.False(check.IsError is true);
        Assert.Equal(Cli("processes", "--assembly", ShopAssembly, "--check", ProcessSnapshot), Text(check));
    }

    [Fact]
    public async Task a_changed_process_is_reported_in_the_text_like_the_cli_not_as_a_tool_error()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, File.ReadAllText(ProcessSnapshot).Replace("    - \"step Shipping.Ship\"\n", "", StringComparison.Ordinal));
            await using var session = await McpTestSession.StartAsync();
            var check = await session.Client.CallToolAsync("processes", new Dictionary<string, object?>
            {
                ["assemblyPath"] = ShopAssembly,
                ["snapshotPath"] = path,
            }, cancellationToken: session.Token);

            Assert.False(check.IsError is true);
            Assert.StartsWith("Changed process: OrderFulfillment", Text(check), StringComparison.Ordinal);
            var app = new CommandAppTester();
            app.Configure(config => Program.Configure(config, new TestConsole()));
            Assert.Equal(1, app.Run("processes", "--assembly", ShopAssembly, "--check", path).ExitCode);
            Assert.Equal(app.Console.Output, Text(check));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task timeline_and_generated_test_match_cli_for_a_real_shop_history()
    {
        var runtime = ShopModule.CreateRuntime();
        await runtime.SendAsync(new Restock("KEYBOARD", 2));
        await runtime.SendAsync(new PlaceOrder("MCP-1", "KEYBOARD", 1, 89m));
        await runtime.PublishAsync(new PaymentConfirmed("MCP-1"));
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, runtime.HistoryOf(ShopProcesses.OrderFulfillment, "MCP-1")!.ToJson());
            await using var session = await McpTestSession.StartAsync();
            var timeline = await session.Client.CallToolAsync("replay", new Dictionary<string, object?> { ["historyPath"] = path }, cancellationToken: session.Token);
            Assert.False(timeline.IsError is true);
            Assert.Equal(Cli("replay", path), Text(timeline));
            var test = await session.Client.CallToolAsync("replay", new Dictionary<string, object?>
            {
                ["historyPath"] = path,
                ["asTest"] = true,
                ["runtime"] = "ShopModule.CreateRuntime",
                ["process"] = "Processes.OrderFulfillment",
                ["namespace"] = "Shop.Tests",
                ["usings"] = TestUsings,
            }, cancellationToken: session.Token);
            Assert.False(test.IsError is true);
            Assert.Equal(Cli("replay", path, "--as-test", "--runtime", "ShopModule.CreateRuntime", "--process", "Processes.OrderFulfillment",
                "--namespace", "Shop.Tests", "--using", "Shop", "--using", "Shop.Fulfillment"), Text(test));
            var defaults = await session.Client.CallToolAsync("replay", new Dictionary<string, object?>
            {
                ["historyPath"] = path,
                ["asTest"] = true,
                ["runtime"] = "R",
                ["process"] = "P",
            }, cancellationToken: session.Token);
            Assert.Equal(Cli("replay", path, "--as-test", "--runtime", "R", "--process", "P"), Text(defaults));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task tool_errors_keep_cli_messages_and_the_server_keeps_answering()
    {
        await using var session = await McpTestSession.StartAsync();
        var missing = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.missing");
        foreach (var (tool, argument, command, option) in new[]
        {
            ("diagram", "assemblyPath", "diagram", "--assembly"),
            ("processes", "assemblyPath", "processes", "--assembly"),
            ("asyncapi", "contractsPath", "asyncapi", "--contracts"),
            ("contracts", "contractsPath", "asyncapi", "--contracts"),
            ("replay", "historyPath", "replay", (string?)null),
        })
        {
            var result = await session.Client.CallToolAsync(tool, new Dictionary<string, object?> { [argument] = missing }, cancellationToken: session.Token);
            Assert.True(result.IsError);
            var errors = new TestConsole();
            errors.Profile.Width = 500;
            var app = new CommandAppTester();
            app.Configure(config => Program.Configure(config, errors));
            var args = option is null ? new[] { command, missing } : new[] { command, option, missing };
            Assert.NotEqual(0, app.Run(args).ExitCode);
            Assert.Contains(Text(result), errors.Output, StringComparison.Ordinal);
        }

        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "# comment\ninvalid");
            var invalid = await session.Client.CallToolAsync("asyncapi", new Dictionary<string, object?> { ["contractsPath"] = path }, cancellationToken: session.Token);
            Assert.True(invalid.IsError);
            Assert.Equal("Invalid contract snapshot at line 2: expected a contract name followed by a positive version (v1, v2, ...).", Text(invalid));
            var invalidHistory = await session.Client.CallToolAsync("replay", new Dictionary<string, object?> { ["historyPath"] = path }, cancellationToken: session.Token);
            Assert.True(invalidHistory.IsError);
            var errors = new TestConsole();
            errors.Profile.Width = 1000;
            var app = new CommandAppTester();
            app.Configure(config => Program.Configure(config, errors));
            Assert.NotEqual(0, app.Run("replay", path).ExitCode);
            Assert.Contains(Text(invalidHistory), errors.Output, StringComparison.Ordinal);
            var missingOptions = await session.Client.CallToolAsync("replay", new Dictionary<string, object?>
            {
                ["historyPath"] = path,
                ["asTest"] = true,
            }, cancellationToken: session.Token);
            Assert.True(missingOptions.IsError);
            Assert.Equal("--as-test needs --runtime and --process: the test calls Urd.ReplayAsync(runtime, process, history).", Text(missingOptions));
            var stillAlive = await session.Client.CallToolAsync("diagram", new Dictionary<string, object?> { ["assemblyPath"] = ShopAssembly }, cancellationToken: session.Token);
            Assert.False(stillAlive.IsError is true);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task stdio_client_lists_tools_calls_shop_and_keeps_diagnostics_off_stdout()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var diagnostics = new ConcurrentQueue<string>();
        await using (var client = await McpClient.CreateAsync(new StdioClientTransport(new StdioClientTransportOptions
        {
            Command = "dotnet",
            Arguments = [typeof(Program).Assembly.Location, "mcp"],
            EnvironmentVariables = new Dictionary<string, string?> { ["WYRD_MCP_TEST_STDOUT"] = "1" },
            StandardErrorLines = diagnostics.Enqueue,
        }), cancellationToken: timeout.Token))
        {
            var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
            output.WriteLine($"tools/list: {string.Join(", ", tools.Select(tool => tool.Name).Order(StringComparer.Ordinal))}");
            Assert.Equal(5, tools.Count);
            var result = await client.CallToolAsync("diagram", new Dictionary<string, object?> { ["assemblyPath"] = ShopAssembly }, cancellationToken: timeout.Token);
            Assert.False(result.IsError is true);
            Assert.Equal(Cli("diagram", "--assembly", ShopAssembly), Text(result));
            output.WriteLine("tools/call diagram:");
            output.WriteLine(Text(result));

            var noisy = await client.CallToolAsync("diagram", new Dictionary<string, object?>
            {
                ["assemblyPath"] = typeof(TestProcesses).Assembly.Location,
            }, cancellationToken: timeout.Token);
            Assert.False(noisy.IsError is true);
            Assert.Equal(Cli("diagram", "--assembly", typeof(TestProcesses).Assembly.Location), Text(noisy));
            Assert.Equal(5, (await client.ListToolsAsync(cancellationToken: timeout.Token)).Count);
        }

        Assert.Contains("Process discovery diagnostic", diagnostics);
    }

    private static string Text(CallToolResult result) => Assert.Single(result.Content.OfType<TextContentBlock>()).Text;

    private static string Cli(params string[] args)
    {
        var errors = new TestConsole();
        var app = new CommandAppTester();
        app.Configure(config => Program.Configure(config, errors));
        Assert.Equal(0, app.Run(args).ExitCode);
        Assert.Empty(errors.Output);
        return app.Console.Output;
    }
}
