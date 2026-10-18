using System.Data.Common;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Wyrd.EntityFrameworkCore;

/// <summary>
/// What differs between databases: the one raw query (taking the next due message so that workers never take the same
/// one) and how a database says "someone else wrote this first".
/// </summary>
internal abstract class Dialect
{
    public static Dialect For(DatabaseFacade database) => database.ProviderName switch
    {
        "Npgsql.EntityFrameworkCore.PostgreSQL" => new PostgreSql(),
        "Microsoft.EntityFrameworkCore.SqlServer" => new SqlServer(),
        var other => throw new NotSupportedException(
            $"Wyrd's EF Core host supports PostgreSQL and SQL Server, not '{other}': it needs a queue that workers can share safely."),
    };

    /// <summary>The next due message, locked for this transaction and skipped by the others. <c>{0}</c> is now.</summary>
    public abstract string Dequeue(string table);

    /// <summary>The quoted name of a table.</summary>
    public abstract string Table(string schema, string name);

    /// <summary>A unique key, serialization or deadlock failure: another transaction got there first, so retry.</summary>
    public bool IsConflict(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is DbException db && IsConflict(db))
            {
                return true;
            }
        }

        return false;
    }

    protected abstract bool IsConflict(DbException exception);

    private sealed class PostgreSql : Dialect
    {
        public override string Dequeue(string table) =>
            $"SELECT id, envelope, available_at, attempts, last_error FROM {table} WHERE available_at <= {{0}} ORDER BY available_at, id LIMIT 1 FOR UPDATE SKIP LOCKED";

        public override string Table(string schema, string name) => $"\"{schema}\".\"{name}\"";

        // 23505 unique_violation, 40001 serialization_failure, 40P01 deadlock_detected.
        protected override bool IsConflict(DbException exception) => exception.SqlState is "23505" or "40001" or "40P01";
    }

    private sealed class SqlServer : Dialect
    {
        private static PropertyInfo? _number;

        public override string Dequeue(string table) =>
            $"SELECT TOP (1) id, envelope, available_at, attempts, last_error FROM {table} WITH (UPDLOCK, READPAST, ROWLOCK) WHERE available_at <= {{0}} ORDER BY available_at, id";

        public override string Table(string schema, string name) => $"[{schema}].[{name}]";

        // 2627/2601 unique key violation, 1205 deadlock victim. SqlException exposes the number only on its own type, and
        // this package doesn't reference the SQL Server client (PostgreSQL users don't need it), so it is read by name.
        protected override bool IsConflict(DbException exception)
        {
            if (exception.GetType().FullName != "Microsoft.Data.SqlClient.SqlException")
            {
                return false;
            }

            _number ??= exception.GetType().GetProperty("Number");
            return _number?.GetValue(exception) is 2627 or 2601 or 1205;
        }
    }
}
