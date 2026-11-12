using Npgsql;

namespace Wyrd.Benchmarks.Load;

/// <summary>
/// What the database does alone, at the same kind of rate: one insert per transaction, on a connection from a pool, at
/// <c>--rate</c> per second. A Wyrd message is a few statements and a commit; this is one statement and a commit, so
/// it reads as the ceiling a message can't beat on this machine.
/// </summary>
internal static class Ceiling
{
    public static async Task<int> RunAsync(string connection, Options options)
    {
        var rate = options.Decimal("rate", 1_000);
        var warmup = TimeSpan.FromSeconds(options.Number("warmup", 10));
        var duration = TimeSpan.FromSeconds(options.Number("duration", 60));
        var loop = new OpenLoop(rate, warmup, duration, options.Number("max-in-flight", 5_000));
        await using var source = NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(connection) { MaxPoolSize = options.Number("pool", 100) }.ConnectionString);
        await using (var create = source.CreateCommand("CREATE TABLE IF NOT EXISTS bench_ceiling (id bigserial PRIMARY KEY, at timestamptz NOT NULL DEFAULT now(), payload text NOT NULL)"))
        {
            await create.ExecuteNonQueryAsync();
        }

        var committed = new Latencies();
        long errors = 0;
        await loop.RunAsync(async (index, due, measured) =>
        {
            try
            {
                await using var connection = await source.OpenConnectionAsync();
                await using var transaction = await connection.BeginTransactionAsync();
                await using var insert = new NpgsqlCommand("INSERT INTO bench_ceiling (payload) VALUES ($1)", connection, transaction);
                insert.Parameters.Add(new NpgsqlParameter { Value = "{\"order\":" + index + "}" });
                await insert.ExecuteNonQueryAsync();
                await transaction.CommitAsync();
            }
            catch (NpgsqlException)
            {
                Interlocked.Increment(ref errors);
                return;
            }

            if (measured)
            {
                committed.Add(due);
            }
        });

        Console.WriteLine();
        Console.WriteLine("| Inserts/s asked | Inserts/s committed | p50 ms | p99 | max | Errors | Dropped |");
        Console.WriteLine("| ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
        Console.WriteLine($"| {rate:F0} | {committed.Count / duration.TotalSeconds:F0} | {committed.Percentile(0.50)} | {committed.Percentile(0.99)} | "
            + $"{committed.Max} | {errors:N0} | {loop.Dropped:N0} |");
        return errors > 0 || loop.Dropped > 0 ? 1 : 0;
    }
}
