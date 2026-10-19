using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Wyrd.Tool;

internal static class WyrdMcpServer
{
    public static McpServer Create(ITransport transport) => McpServer.Create(transport, new McpServerOptions
    {
        ServerInfo = new Implementation
        {
            Name = "wyrd",
            Version = typeof(WyrdMcpServer).Assembly.GetName().Version!.ToString(),
        },
        ServerInstructions = "Paths resolve relative to the server's working directory. The tools return documents without writing output files.",
        ToolCollection = new McpServerPrimitiveCollection<McpServerTool>
        {
            McpServerTool.Create(WyrdMcpTools.Diagram),
            McpServerTool.Create(WyrdMcpTools.AsyncApi),
            McpServerTool.Create(WyrdMcpTools.Contracts),
            McpServerTool.Create(WyrdMcpTools.Replay),
        },
    });
}
