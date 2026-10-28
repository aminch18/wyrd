using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Shop.Contracts;
using Shop.Inventory;
using Shop.Orders;

namespace Wyrd.Benchmarks.Concurrency;

/// <summary>
/// Where a worker's time goes (<c>--profile</c>): one worker handles a backlog of orders, and every database command and
/// commit it runs is counted and timed. Measured on the client: a command's time includes the round trip.
/// </summary>
internal sealed partial class Profile
{
    private readonly ConcurrentDictionary<string, Statement> _statements = new();
    private long _commits;
    private long _commitTicks;
    private long _transactions;
    private long _savepoints;

    public Profile()
    {
        Commands = new CommandTimer(this);
        Transactions = new CommitTimer(this);
    }

    public IInterceptor Commands { get; }

    public IInterceptor Transactions { get; }

    public static async Task RunAsync(Server server, int orders)
    {
        var profile = new Profile();
        var (plain, profiled) = await server.NewDatabaseAsync(profile);
        var conflicts = new Conflicts();
        using var shop = Server.Shop(plain, conflicts);
        for (var index = 0; index < orders; index++)
        {
            var id = "O-" + index.ToString(CultureInfo.InvariantCulture);
            await shop.SendAsync(new Restock("SKU-" + id, 1));
            await shop.SendAsync(new PlaceOrder(id, "SKU-" + id, 1, 10m));
            await shop.PublishAsync(new PaymentConfirmed(id));
        }

        var roundTrip = await RoundTripAsync(plain);
        using var worker = Server.Shop(profiled, conflicts);
        using var stop = new CancellationTokenSource();
        using var handled = new WorkerLoad.Handled();
        var clock = Stopwatch.StartNew();
        var running = worker.RunAsync(stop.Token);
        while (await shop.HealthAsync() is { Due: > 0 } or { PendingSteps: > 0 })
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20));
        }

        clock.Stop();
        await stop.CancelAsync();
        await running;
        profile.Print(orders, clock.Elapsed, handled.Count, roundTrip);
    }

    /// <summary>The cheapest round trip to this server (<c>SELECT 1</c>, mean of 1,000): the floor of every command.</summary>
    private static async Task<TimeSpan> RoundTripAsync(Microsoft.EntityFrameworkCore.IDbContextFactory<Wyrd.EntityFrameworkCore.WyrdDbContext> contexts)
    {
        await using var db = await contexts.CreateDbContextAsync();
        var connection = Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.GetDbConnection(db.Database);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1";
        await command.ExecuteScalarAsync();
        var clock = Stopwatch.StartNew();
        for (var i = 0; i < 1_000; i++)
        {
            await command.ExecuteScalarAsync();
        }

        return clock.Elapsed / 1_000;
    }

    private void Print(int orders, TimeSpan elapsed, long handled, TimeSpan roundTrip)
    {
        var statements = _statements.Values.ToList();
        var commands = statements.Sum(s => s.Count);
        var commandTime = TimeSpan.FromTicks(statements.Sum(s => s.Ticks));
        var commitTime = TimeSpan.FromTicks(_commitTicks);
        var transactions = Math.Max(1, _transactions);
        Console.WriteLine($"## Profile: one worker, {orders:N0} orders ({handled:N0} messages, {transactions:N0} transactions)");
        Console.WriteLine();
        Console.WriteLine($"Cheapest round trip (SELECT 1): {roundTrip.TotalMilliseconds:F3} ms");
        Console.WriteLine();
        Console.WriteLine("| | Total | Per transaction | Share of the time |");
        Console.WriteLine("| --- | ---: | ---: | ---: |");
        Console.WriteLine($"| Wall time | {elapsed.TotalSeconds:F2} s | {elapsed.TotalMilliseconds / transactions:F2} ms | 100% |");
        Console.WriteLine($"| Commands (round trips) | {commands:N0} | {commands / (double)transactions:F1} | {commandTime / elapsed:P0} |");
        Console.WriteLine($"| Commits | {_commits:N0} | {_commits / (double)transactions:F1} | {commitTime / elapsed:P0} |");
        Console.WriteLine($"| Savepoints (created and released by EF Core) | {_savepoints:N0} | {_savepoints / (double)transactions:F1} | |");
        Console.WriteLine($"| Everything else (engine, EF Core, JSON) | | {(elapsed - commandTime - commitTime).TotalMilliseconds / transactions:F2} ms | {(elapsed - commandTime - commitTime) / elapsed:P0} |");
        Console.WriteLine();
        Console.WriteLine("| Statement | Count | Per transaction | Mean ms |");
        Console.WriteLine("| --- | ---: | ---: | ---: |");
        foreach (var statement in statements.OrderByDescending(s => s.Ticks).Take(15))
        {
            Console.WriteLine($"| `{statement.Text}` | {statement.Count:N0} | {statement.Count / (double)transactions:F2} | "
                + $"{TimeSpan.FromTicks(statement.Ticks / statement.Count).TotalMilliseconds:F2} |");
        }
    }

    private void Record(DbCommand command, TimeSpan duration)
    {
        var statement = _statements.GetOrAdd(Shape(command.CommandText), text => new Statement(text));
        Interlocked.Increment(ref statement.Count);
        Interlocked.Add(ref statement.Ticks, duration.Ticks);
    }

    /// <summary>A statement's kind and table, e.g. <c>INSERT wyrd.queue</c>: parameters and batches don't split it.</summary>
    private static string Shape(string sql)
    {
        var shapes = Verb().Matches(sql).Select(m => $"{m.Groups[1].Value.ToUpperInvariant()} {m.Groups[2].Value.Replace("\"", "")}").Distinct();
        return string.Join(" + ", shapes) is { Length: > 0 } shape ? shape : sql.Split('\n')[0];
    }

    [GeneratedRegex(@"\b(SELECT|INSERT INTO|UPDATE|DELETE FROM)\b[^;]*?\b(?:FROM|INTO|UPDATE)?\s*(""?wyrd""?\.""?\w+""?)", RegexOptions.IgnoreCase)]
    private static partial Regex Verb();

    private sealed class Statement(string text)
    {
        public string Text { get; } = text;

        public long Count;

        public long Ticks;
    }

    private sealed class CommandTimer(Profile profile) : DbCommandInterceptor
    {
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            profile.Record(command, eventData.Duration);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            profile.Record(command, eventData.Duration);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<object?> ScalarExecutedAsync(DbCommand command, CommandExecutedEventData eventData, object? result, CancellationToken cancellationToken = default)
        {
            profile.Record(command, eventData.Duration);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class CommitTimer(Profile profile) : DbTransactionInterceptor
    {
        public override ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection, TransactionEndEventData eventData, DbTransaction result, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref profile._transactions);
            return ValueTask.FromResult(result);
        }

        public override Task CreatedSavepointAsync(DbTransaction transaction, TransactionEventData eventData, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref profile._savepoints);
            return Task.CompletedTask;
        }

        public override Task ReleasedSavepointAsync(DbTransaction transaction, TransactionEventData eventData, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref profile._savepoints);
            return Task.CompletedTask;
        }

        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref profile._commits);
            Interlocked.Add(ref profile._commitTicks, eventData.Duration.Ticks);
            return Task.CompletedTask;
        }
    }
}
