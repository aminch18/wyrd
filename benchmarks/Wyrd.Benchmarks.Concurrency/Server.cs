using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;
using Npgsql;
using Shop;
using Wyrd.EntityFrameworkCore;
using Wyrd.Runtime;

namespace Wyrd.Benchmarks.Concurrency;

/// <summary>The PostgreSQL server: every run gets a new, empty database with Wyrd's tables.</summary>
internal sealed class Server(string connectionString)
{
    private int _databases;

    private static readonly Retention KeepEverything = new()
    {
        Deduplication = Keep.Forever("a benchmark run is short"),
        FinishedInstances = Keep.Forever("a benchmark run is short"),
        RelayedMessages = Keep.Forever("a benchmark run is short"),
        Events = Keep.Forever("a benchmark run is short"),
        DeadLetters = Keep.Forever("a benchmark run is short"),
    };

    public async Task<string> VersionAsync()
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        return connection.PostgreSqlVersion.ToString();
    }

    /// <summary>A new database, and a factory of contexts on it.</summary>
    public async Task<IDbContextFactory<WyrdDbContext>> NewDatabaseAsync() => (await NewDatabaseAsync(null)).Plain;

    /// <summary>
    /// A new database, and two factories of contexts on it: a plain one, and one whose commands and commits
    /// <paramref name="profile"/> measures.
    /// </summary>
    public async Task<(IDbContextFactory<WyrdDbContext> Plain, IDbContextFactory<WyrdDbContext> Profiled)> NewDatabaseAsync(Profile? profile)
    {
        // Each database has a pool of its own: the last run's idle connections are closed, not left to fill the server.
        NpgsqlConnection.ClearAllPools();
        var database = new NpgsqlConnectionStringBuilder(connectionString) { Database = $"run{Interlocked.Increment(ref _databases)}" };
        // Pooled, as an application should register it (AddPooledDbContextFactory): a context per message is cheap.
        var plain = new PooledDbContextFactory<WyrdDbContext>(new DbContextOptionsBuilder<WyrdDbContext>().UseNpgsql(database.ConnectionString).Options);
        var profiled = profile is null
            ? plain
            : new PooledDbContextFactory<WyrdDbContext>(
                new DbContextOptionsBuilder<WyrdDbContext>().UseNpgsql(database.ConnectionString).AddInterceptors(profile.Commands, profile.Transactions).Options);
        await using var db = plain.CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        return (plain, profiled);
    }

    /// <summary>
    /// The Shop on <paramref name="contexts"/>, as one instance of an application would run it: one runtime per process.
    /// </summary>
    public static EfCoreRuntime<WyrdDbContext> Shop(IDbContextFactory<WyrdDbContext> contexts, Conflicts conflicts) =>
        ShopModule.Register(new EfCoreRuntime<WyrdDbContext>(contexts, ShopJson.Default, KeepEverything, logger: conflicts));
}

/// <summary>
/// Counts the concurrency conflicts the runtime retried, from its own log: a command retried by <c>SendAsync</c>, or a
/// message rolled back and handled again by a worker. Nothing else is logged.
/// </summary>
internal sealed class Conflicts : ILogger<EfCoreRuntime<WyrdDbContext>>
{
    private int _retried;

    public int Retried => Volatile.Read(ref _retried);

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel == LogLevel.Debug;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        // The runtime's source-generated log events are named after their methods: no message is formatted.
        if (eventId.Name is "LogConflict" or "LogConflictRetry")
        {
            Interlocked.Increment(ref _retried);
        }
    }
}
