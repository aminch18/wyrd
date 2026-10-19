using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Spectre.Console.Cli;
using Wyrd.Runtime;

namespace Wyrd.Tool;

internal sealed record ContractSnapshot(IReadOnlyList<Contract> Contracts);

internal static class WyrdMcpTools
{
    [McpServerTool(Name = "diagram", ReadOnly = true, OpenWorld = false)]
    [Description("Generate Markdown with Mermaid diagrams for every public static Wyrd process in a trusted compiled .NET assembly. Use this to inspect the real workflow before changing or explaining it; returns the same document as dotnet wyrd diagram.")]
    public static CallToolResult Diagram(
        [Description("Path to the compiled assembly, with its dependencies alongside it; for example samples/Shop/bin/Release/net10.0/Shop.dll.")] string assemblyPath,
        CancellationToken cancellationToken = default) => Document(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        Validate(new DiagramCommand.Settings { AssemblyPath = assemblyPath });
        return Diagrams.Generate(assemblyPath);
    });

    [McpServerTool(Name = "asyncapi", ReadOnly = true, OpenWorld = false)]
    [Description("Generate AsyncAPI 3.0 YAML from the reviewed integration-event contract snapshot. Use this to document public messages without guessing from internal domain events; returns the same document as dotnet wyrd asyncapi.")]
    public static CallToolResult AsyncApi(
        [Description("Path to contracts.wyrd.txt. Defaults to that file in the server's working directory.")] string contractsPath = "contracts.wyrd.txt",
        [Description("API title; defaults to the snapshot's containing folder name.")] string? title = null,
        [Description("API document version, independent of individual event versions. Defaults to 0.0.0.")] string version = "0.0.0",
        CancellationToken cancellationToken = default) => Document(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        Validate(new AsyncApiCommand.Settings { ContractsPath = contractsPath });
        var path = Path.GetFullPath(contractsPath);
        return Wyrd.Tool.AsyncApi.Generate(Wyrd.Tool.Contracts.Parse(File.ReadAllText(path)), title ?? new FileInfo(path).Directory!.Name, version);
    });

    [McpServerTool(Name = "contracts", ReadOnly = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ContractSnapshot))]
    [Description("Read the reviewed integration-event snapshot as structured contracts: names, versions, retired markers and fields. Use this before publishing or consuming an integration event; uses the same parser as dotnet wyrd asyncapi.")]
    public static CallToolResult Contracts(
        [Description("Path to contracts.wyrd.txt. Defaults to that file in the server's working directory.")] string contractsPath = "contracts.wyrd.txt",
        CancellationToken cancellationToken = default) => Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        Validate(new AsyncApiCommand.Settings { ContractsPath = contractsPath });
        var contracts = new ContractSnapshot(Wyrd.Tool.Contracts.Parse(File.ReadAllText(contractsPath)));
        var content = JsonSerializer.SerializeToElement(contracts, McpJsonUtilities.DefaultOptions);
        return new CallToolResult
        {
            StructuredContent = content,
            Content = [new TextContentBlock { Text = content.GetRawText() }],
        };
    });

    [McpServerTool(Name = "replay", ReadOnly = true, OpenWorld = false)]
    [Description("Render an exported Wyrd instance history as its recorded timeline, or generate an xUnit regression test that replays it with Urd. Use this to investigate a real execution or preserve it as a test; returns the same output as dotnet wyrd replay and does not execute the generated C#.")]
    public static CallToolResult Replay(
        [Description("Path to a JSON history exported with InstanceHistory.ToJson().")] string historyPath,
        [Description("False for the recorded timeline; true to generate an xUnit test, requiring runtime and process.")] bool asTest = false,
        [Description("C# runtime factory expression for the generated test, e.g. ShopModule.CreateRuntime. Required when asTest is true.")] string? runtime = null,
        [Description("C# process expression for the generated test, e.g. Processes.OrderFulfillment. Required when asTest is true.")] string? process = null,
        [Description("Namespace of the generated test. Defaults to Replays.")] string @namespace = "Replays",
        [Description("Additional C# using namespaces for the generated test, e.g. Shop and Shop.Fulfillment.")] string[]? usings = null,
        CancellationToken cancellationToken = default) => Document(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        Validate(new ReplayCommand.Settings { HistoryPath = historyPath, AsTest = asTest, Runtime = runtime, Process = process });
        var json = File.ReadAllText(historyPath);
        var history = InstanceHistory.FromJson(json);
        return asTest
            ? ReplayTest.Generate(history, json, runtime!, process!, @namespace, usings ?? [], Path.GetFileName(historyPath))
            : ReplayTest.Timeline(history);
    });

    private static void Validate(CommandSettings settings)
    {
        var result = settings.Validate();
        if (!result.Successful)
        {
            throw new McpException(result.Message!);
        }
    }

    private static CallToolResult Document(Func<string> generate) => Run(() =>
        new CallToolResult { Content = [new TextContentBlock { Text = generate() }] });

    private static CallToolResult Run(Func<CallToolResult> generate)
    {
        try
        {
            return generate();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Return the CLI's exact message without the SDK's invocation-error prefix.
            return new CallToolResult
            {
                IsError = true,
                Content = [new TextContentBlock { Text = exception.GetBaseException().Message }],
            };
        }
    }
}
