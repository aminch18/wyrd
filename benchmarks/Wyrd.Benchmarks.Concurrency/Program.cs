using System.Globalization;
using Npgsql;
using Testcontainers.PostgreSql;
using Wyrd.Benchmarks.Concurrency;

// The durable host under concurrent load, on a real PostgreSQL: by default a container (Docker required), or the
// database of --connection. Throughput and latency are reported, never gated: they depend on the machine.
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
var connection = Option("--connection");
var commands = int.Parse(Option("--commands") ?? "2000", CultureInfo.InvariantCulture);
var orders = int.Parse(Option("--orders") ?? "1000", CultureInfo.InvariantCulture);

PostgreSqlContainer? container = null;
if (connection is null)
{
    // --synchronous-commit off: a commit doesn't wait for the disk (never in production; it shows what the wait costs).
    var settings = new List<string> { "-c", "max_connections=300" };
    if (Option("--synchronous-commit") is { } synchronous)
    {
        settings.AddRange(["-c", $"synchronous_commit={synchronous}"]);
    }

    container = new PostgreSqlBuilder("postgres:17-alpine").WithCommand([.. settings]).Build();
    await container.StartAsync();
    connection = container.GetConnectionString();
}

try
{
    await using var server = new Server(new NpgsqlConnectionStringBuilder(connection) { MaxPoolSize = 100 }.ConnectionString);
    Console.WriteLine($"PostgreSQL {await server.VersionAsync()}, .NET {Environment.Version}, {Environment.ProcessorCount} cores, "
        + "connection pool of 100 per process");
    Console.WriteLine();
    if (args.Contains("--profile"))
    {
        await Profile.RunAsync(server, orders);
        return;
    }

    await CommandLoad.RunAsync(server, commands, [1, 8, 32, 128]);
    Console.WriteLine();
    await WorkerLoad.RunAsync(server, orders, [1, 2, 4, 8]);
}
finally
{
    if (container is not null)
    {
        await container.DisposeAsync();
    }
}

string? Option(string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}
