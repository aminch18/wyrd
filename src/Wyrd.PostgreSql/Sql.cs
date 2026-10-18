using System.Text.RegularExpressions;

namespace Wyrd.PostgreSql;

/// <summary>Wyrd's tables, in the application's schema. Payloads are text, not jsonb: jsonb reorders keys, and
/// polymorphic JSON needs its <c>$type</c> discriminator first.</summary>
internal sealed partial class Sql
{
    public Sql(string schema)
    {
        if (!Identifier().IsMatch(schema))
        {
            throw new ArgumentException($"'{schema}' is not a valid schema name: use lower-case letters, digits and underscores.", nameof(schema));
        }

        S = $"\"{schema}\"";
    }

    /// <summary>The quoted schema, validated above.</summary>
    public string S { get; }

    public string CreateSchema => $"""
        CREATE SCHEMA IF NOT EXISTS {S};
        CREATE TABLE IF NOT EXISTS {S}.states (
            state_type text NOT NULL, key text NOT NULL, type text NOT NULL, payload text NOT NULL, version bigint NOT NULL,
            PRIMARY KEY (state_type, key));
        CREATE TABLE IF NOT EXISTS {S}.instances (
            process text NOT NULL, key text NOT NULL, status text NOT NULL, deadline timestamptz,
            data text NOT NULL, version bigint NOT NULL, created bigserial,
            PRIMARY KEY (process, key));
        CREATE TABLE IF NOT EXISTS {S}.events (
            seq bigserial PRIMARY KEY, type text NOT NULL, payload text NOT NULL, recorded_at timestamptz NOT NULL);
        CREATE TABLE IF NOT EXISTS {S}.published (
            seq bigserial PRIMARY KEY, type text NOT NULL, payload text NOT NULL, recorded_at timestamptz NOT NULL);
        CREATE TABLE IF NOT EXISTS {S}.inbox (
            message_id uuid PRIMARY KEY, reply text NOT NULL, recorded_at timestamptz NOT NULL);
        CREATE TABLE IF NOT EXISTS {S}.handled (
            message_id uuid PRIMARY KEY);
        CREATE TABLE IF NOT EXISTS {S}.parked (
            process text NOT NULL, key text NOT NULL, step int NOT NULL, type text NOT NULL, payload text NOT NULL,
            PRIMARY KEY (process, key, step));
        CREATE TABLE IF NOT EXISTS {S}.views (
            view_type text NOT NULL, key text NOT NULL, type text NOT NULL, payload text NOT NULL, created bigserial,
            PRIMARY KEY (view_type, key));
        CREATE TABLE IF NOT EXISTS {S}.queue (
            id bigserial PRIMARY KEY, envelope text NOT NULL, available_at timestamptz NOT NULL,
            attempts int NOT NULL DEFAULT 0, last_error text);
        CREATE INDEX IF NOT EXISTS queue_available ON {S}.queue (available_at, id);
        CREATE TABLE IF NOT EXISTS {S}.dead_letters (
            id bigint PRIMARY KEY, envelope text NOT NULL, attempts int NOT NULL, error text, failed_at timestamptz NOT NULL);
        """;

    [GeneratedRegex("^[a-z_][a-z0-9_]{0,62}$")]
    private static partial Regex Identifier();
}
