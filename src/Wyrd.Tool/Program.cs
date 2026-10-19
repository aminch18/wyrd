using Spectre.Console;
using Spectre.Console.Cli;

namespace Wyrd.Tool;

internal static class Program
{
    public static int Main(string[] args)
    {
        var errors = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(Console.Error) });
        var app = new CommandApp();
        app.Configure(config => Configure(config, errors));
        return app.Run(args);
    }

    internal static void Configure(IConfigurator config, IAnsiConsole errors)
    {
        config.SetApplicationName("wyrd");
        config.AddCommand<McpCommand>("mcp").WithDescription("Serve diagrams, contracts, AsyncAPI and replay over MCP stdio.");
        config.AddCommand<AsyncApiCommand>("asyncapi")
            .WithDescription("Generate AsyncAPI 3.0 YAML from an integration contract snapshot.")
            .WithExample("asyncapi", "--contracts", "contracts.wyrd.txt", "--output", "asyncapi.yaml");
        config.AddCommand<DiagramCommand>("diagram")
            .WithDescription("Generate Mermaid documentation from public static processes in an assembly.")
            .WithExample("diagram", "--assembly", "Shop.dll", "--output", "processes.md");
        config.AddCommand<ReplayCommand>("replay")
            .WithDescription("Print a recorded instance history, or turn it into a regression test with --as-test (Urd).")
            .WithExample("replay", "A-1.history.json")
            .WithExample("replay", "A-1.history.json", "--as-test", "--runtime", "ShopModule.CreateRuntime",
                "--process", "Processes.OrderFulfillment", "--using", "Shop", "--using", "Shop.Fulfillment", "--output", "A1Replay.cs");
        config.SetExceptionHandler((exception, _) =>
        {
            errors.MarkupLineInterpolated($"[red]wyrd:[/] {exception.GetBaseException().Message}");
            return 1;
        });
    }
}
