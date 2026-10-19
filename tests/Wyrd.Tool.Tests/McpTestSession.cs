using System.IO.Pipelines;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using MessagePipe = System.IO.Pipelines.Pipe;

namespace Wyrd.Tool.Tests;

internal sealed class McpTestSession : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new(TimeSpan.FromSeconds(20));
    private readonly McpServer _server;
    private readonly Task _running;

    private McpTestSession()
    {
        var input = new MessagePipe();
        var output = new MessagePipe();
        _server = WyrdMcpServer.Create(new StreamServerTransport(input.Reader.AsStream(), output.Writer.AsStream(), "wyrd-tests"));
        _running = _server.RunAsync(_stop.Token);
        Transport = new StreamClientTransport(input.Writer.AsStream(), output.Reader.AsStream());
    }

    private StreamClientTransport Transport { get; }
    public McpClient Client { get; private set; } = null!;
    public CancellationToken Token => _stop.Token;

    public static async Task<McpTestSession> StartAsync()
    {
        var session = new McpTestSession();
        session.Client = await McpClient.CreateAsync(session.Transport, cancellationToken: session.Token);
        return session;
    }

    public async ValueTask DisposeAsync()
    {
        await Client.DisposeAsync();
        await _stop.CancelAsync();
        try
        {
            await _running;
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }

        await _server.DisposeAsync();
        _stop.Dispose();
    }
}
