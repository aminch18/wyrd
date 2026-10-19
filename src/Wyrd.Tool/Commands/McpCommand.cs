using ModelContextProtocol.Server;
using Spectre.Console.Cli;

namespace Wyrd.Tool;

internal sealed class McpCommand : AsyncCommand<McpCommand.Settings>
{
    public sealed class Settings : CommandSettings;

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        // The SDK writes protocol frames to the raw stdout stream. Any application output,
        // including diagnostics from a loaded assembly's process getters, belongs on stderr.
        var output = Console.Out;
        Console.SetOut(Console.Error);
        try
        {
            await using var server = WyrdMcpServer.Create(new StdioServerTransport("wyrd"));
            await server.RunAsync(cancellationToken).ConfigureAwait(false);
            return 0;
        }
        finally
        {
            Console.SetOut(output);
        }
    }
}
