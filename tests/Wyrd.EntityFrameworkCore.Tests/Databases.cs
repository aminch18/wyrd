using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Shop;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;

namespace Wyrd.EntityFrameworkCore.Tests;

/// <summary>One real database server for the whole run; every test gets a database of its own.</summary>
public abstract class Database : IAsyncLifetime
{
    private int _databases;

    public abstract Task InitializeAsync();

    public abstract Task DisposeAsync();

    /// <summary>A new, empty database with Wyrd's tables (and whatever else <typeparamref name="TContext"/> maps).</summary>
    public async Task<IDbContextFactory<TContext>> NewAsync<TContext>(Func<DbContextOptions<TContext>, TContext> create)
        where TContext : DbContext
    {
        var options = Use(new DbContextOptionsBuilder<TContext>(), $"t{Interlocked.Increment(ref _databases)}").Options;
        var contexts = new Contexts<TContext>(() => create(options));
        await using var db = contexts.CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        return contexts;
    }

    public Task<IDbContextFactory<WyrdDbContext>> NewAsync() => NewAsync(options => new WyrdDbContext(options));

    /// <summary>The sample shop on this database, with source-generated JSON.</summary>
    public static EfCoreRuntime<WyrdDbContext> Shop(IDbContextFactory<WyrdDbContext> contexts, TimeProvider? time = null) =>
        ShopModule.Register(new EfCoreRuntime<WyrdDbContext>(contexts, ShopJson.Default, Options, time));

    public static EfCoreRuntimeOptions Options { get; } = new() { RedeliveryDelay = TimeSpan.FromMilliseconds(10) };

    public static FakeTimeProvider Clock() => new(new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero));

    protected abstract DbContextOptionsBuilder<TContext> Use<TContext>(DbContextOptionsBuilder<TContext> builder, string database)
        where TContext : DbContext;

    private sealed class Contexts<TContext>(Func<TContext> create) : IDbContextFactory<TContext>
        where TContext : DbContext
    {
        public TContext CreateDbContext() => create();
    }
}

[CollectionDefinition(Name)]
public sealed class PostgreSqlServer : ICollectionFixture<PostgreSqlDatabase>
{
    public const string Name = "EF Core on PostgreSQL";
}

public sealed class PostgreSqlDatabase : Database
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public override Task InitializeAsync() => _container.StartAsync();

    public override Task DisposeAsync() => _container.DisposeAsync().AsTask();

    protected override DbContextOptionsBuilder<TContext> Use<TContext>(DbContextOptionsBuilder<TContext> builder, string database) =>
        builder.UseNpgsql(new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = database }.ConnectionString);
}

[CollectionDefinition(Name)]
public sealed class SqlServerServer : ICollectionFixture<SqlServerDatabase>
{
    public const string Name = "EF Core on SQL Server";
}

public sealed class SqlServerDatabase : Database
{
    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public override Task InitializeAsync() => _container.StartAsync();

    public override Task DisposeAsync() => _container.DisposeAsync().AsTask();

    protected override DbContextOptionsBuilder<TContext> Use<TContext>(DbContextOptionsBuilder<TContext> builder, string database) =>
        builder.UseSqlServer(new SqlConnectionStringBuilder(_container.GetConnectionString()) { InitialCatalog = database }.ConnectionString);
}
