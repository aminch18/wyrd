using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Shop;
using Testcontainers.PostgreSql;

namespace Wyrd.PostgreSql.Tests;

[CollectionDefinition(Name)]
public sealed class PostgresDatabase : ICollectionFixture<PostgresFixture>
{
    public const string Name = "PostgreSQL";
}

/// <summary>One real PostgreSQL for the whole test run; every test gets its own schema.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private int _schemas;

    public NpgsqlDataSource DataSource { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        DataSource = NpgsqlDataSource.Create(_container.GetConnectionString());
    }

    public async Task DisposeAsync()
    {
        await DataSource.DisposeAsync();
        await _container.DisposeAsync();
    }

    public PostgresRuntimeOptions NewSchema() =>
        new() { Schema = $"t{Interlocked.Increment(ref _schemas)}", RedeliveryDelay = TimeSpan.FromMilliseconds(10) };

    /// <summary>The sample shop on PostgreSQL, with source-generated JSON.</summary>
    public async Task<PostgresRuntime> ShopAsync(PostgresRuntimeOptions options, TimeProvider? time = null)
    {
        var runtime = ShopModule.Register(new PostgresRuntime(DataSource, ShopJson.Default, options, time));
        await runtime.EnsureSchemaAsync();
        return runtime;
    }

    public static FakeTimeProvider Clock() => new(new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero));
}
