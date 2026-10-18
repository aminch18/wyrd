using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Wyrd.Processes;
using Wyrd.Runtime;

namespace Wyrd.PostgreSql;

/// <summary>
/// Verdandi: the durable Wyrd runtime on PostgreSQL. Every message is handled in one transaction that also takes it
/// off the queue: state, events, inbox, outbox, timers and read models commit together, or not at all. The queue
/// is a table, so any number of workers — in one process or many — share it with <c>FOR UPDATE SKIP LOCKED</c>,
/// and nothing is lost on a crash or a restart.
/// </summary>
public sealed partial class PostgresRuntime : IWyrdBuilder<PostgresRuntime>, IWyrdRuntime
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly Application _application;
    private readonly PostgresRuntimeOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly Sql _sql;

    /// <summary>Source-generated JSON from your <c>[WyrdJsonContext]</c>: no reflection, AOT-safe.</summary>
    public PostgresRuntime(
        NpgsqlDataSource dataSource,
        JsonSerializerContext json,
        PostgresRuntimeOptions? options = null,
        TimeProvider? time = null,
        ILogger<PostgresRuntime>? logger = null)
        : this(dataSource, MessageTypes.FromContext(json ?? throw new ArgumentNullException(nameof(json))), options, time, logger)
    {
    }

    /// <summary>Reflection-based JSON, for development. Use a <c>[WyrdJsonContext]</c> for AOT.</summary>
    [RequiresUnreferencedCode("Reflection-based System.Text.Json. Pass a [WyrdJsonContext] for AOT and trimming.")]
    [RequiresDynamicCode("Reflection-based System.Text.Json. Pass a [WyrdJsonContext] for AOT and trimming.")]
    public PostgresRuntime(
        NpgsqlDataSource dataSource,
        PostgresRuntimeOptions? options = null,
        TimeProvider? time = null,
        ILogger<PostgresRuntime>? logger = null)
        : this(dataSource, MessageTypes.Reflection(), options, time, logger)
    {
    }

    private PostgresRuntime(
        NpgsqlDataSource dataSource, MessageTypes types, PostgresRuntimeOptions? options, TimeProvider? time, ILogger? logger)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _application = new Application(types);
        _options = options ?? new PostgresRuntimeOptions();
        _time = time ?? TimeProvider.System;
        _logger = logger ?? NullLogger.Instance;
        _sql = new Sql(_options.Schema);
    }

    /// <summary>Registers the single owner of a command type.</summary>
    /// <summary>Registers the integration that owns a command carried out by another system (the only I/O).</summary>
    public PostgresRuntime AddIntegration<TCommand, TEvent, TError>(Integration<TCommand, TEvent, TError> integration)
        where TCommand : ICommand
        where TEvent : IDomainEvent
        where TError : notnull
    {
        ArgumentNullException.ThrowIfNull(integration);
        _application.Add(integration);
        return this;
    }

    public PostgresRuntime AddSlice<TCommand, TState, TEvent, TError>(Slice<TCommand, TState, TEvent, TError> slice)
        where TCommand : ICommand
        where TState : IState<TState>
        where TEvent : IDomainEvent
        where TError : notnull
    {
        ArgumentNullException.ThrowIfNull(slice);
        _application.Add(slice);
        return this;
    }

    /// <summary>Registers a mapping from a domain event to an integration event.</summary>
    public PostgresRuntime AddPublication<TDomainEvent, TIntegrationEvent>(Publication<TDomainEvent, TIntegrationEvent> publication)
        where TDomainEvent : IDomainEvent
        where TIntegrationEvent : IIntegrationEvent
    {
        ArgumentNullException.ThrowIfNull(publication);
        _application.Add(publication);
        return this;
    }

    /// <summary>Registers an upcaster from one contract version to the next.</summary>
    public PostgresRuntime AddUpcaster<TFrom, TTo>(Upcaster<TFrom, TTo> upcaster)
        where TFrom : IIntegrationEvent
        where TTo : IIntegrationEvent
    {
        ArgumentNullException.ThrowIfNull(upcaster);
        _application.Add(upcaster);
        return this;
    }

    /// <summary>Registers the projection that owns a read model.</summary>
    public PostgresRuntime AddProjection<TView>(Projection<TView> projection)
        where TView : class
    {
        ArgumentNullException.ThrowIfNull(projection);
        _application.Add(projection);
        return this;
    }

    public PostgresRuntime AddProcess(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        _application.Add(process);
        return this;
    }

    /// <summary>Creates Wyrd's schema and tables if they don't exist. Safe to call on every start.</summary>
    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        await using var command = _dataSource.CreateCommand(_sql.CreateSchema);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Executes a command coming from outside (an API call, a UI action...) in its own transaction and returns the
    /// decision. What it causes — events, process steps — is queued and handled by the workers.
    /// </summary>
    public async Task<Decision> SendAsync(ICommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var (type, payload) = _application.Types.Serialize(command, command.GetType());
        var envelope = new Envelope(Guid.CreateVersion7(), MessageKind.Command, type, payload);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return (await InTransactionAsync(
                    async (engine, _) => await engine.HandleAsync(envelope, cancellationToken).ConfigureAwait(false),
                    cancellationToken).ConfigureAwait(false))!;
            }
            catch (Exception ex) when (IsConflict(ex) && attempt < _options.MaxConflictRetries)
            {
                LogConflictRetry(command.GetType().Name, attempt);
            }
        }
    }

    /// <summary>Delivers a fact from another context (a webhook, a message from another service...).</summary>
    public async Task PublishAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var (type, payload) = _application.Types.Serialize(integrationEvent, integrationEvent.GetType());
        await InTransactionAsync(
            async (_, store) =>
            {
                await store.EnqueueAsync(new Envelope(Guid.CreateVersion7(), MessageKind.Event, type, payload), _time.GetUtcNow(), cancellationToken).ConfigureAwait(false);
                return 0;
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Handles queued messages that are due until none is left (or <paramref name="max"/>), and returns how many.</summary>
    public async Task<int> ProcessPendingAsync(int max = 1_000, CancellationToken cancellationToken = default)
    {
        var handled = 0;
        while (handled < max && await ProcessOneAsync(cancellationToken).ConfigureAwait(false))
        {
            handled++;
        }

        return handled;
    }

    /// <summary>A worker: handles messages as they become due, until cancelled. Run as many as you like.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (await ProcessPendingAsync(cancellationToken: cancellationToken).ConfigureAwait(false) == 0)
                {
                    await Task.Delay(_options.PollInterval, _time, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (NpgsqlException ex)
            {
                // The database is unreachable: wait and try again; queued messages are safe.
                LogDatabaseUnavailable(ex);
                await Task.Delay(_options.PollInterval, _time, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    // ── Operators ────────────────────────────────────────────────────────────────────────────

    public Task ResumeAsync(Process process, object key, CancellationToken cancellationToken = default) =>
        OperateAsync(process, key, (engine, name, k) => engine.ResumeAsync(name, k, cancellationToken), cancellationToken);

    public Task CompensateAsync(Process process, object key, CancellationToken cancellationToken = default) =>
        OperateAsync(process, key, (engine, name, k) => engine.CompensateAsync(name, k, cancellationToken), cancellationToken);

    private async Task OperateAsync(Process process, object key, Func<Engine, string, string, ValueTask> operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(key);
        await InTransactionAsync(
            async (engine, _) =>
            {
                await operation(engine, process.Name, Keys.Of(key)).ConfigureAwait(false);
                return 0;
            },
            cancellationToken).ConfigureAwait(false);
    }

    // ── Queries ──────────────────────────────────────────────────────────────────────────────

    public async Task<ProcessStatus?> StatusOfAsync(Process process, object key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(key);
        await using var command = _dataSource.CreateCommand($"SELECT status FROM {_sql.S}.instances WHERE process = @p AND key = @k");
        command.Parameters.AddWithValue("p", process.Name);
        command.Parameters.AddWithValue("k", Keys.Of(key));
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is string status
            ? Enum.Parse<ProcessStatus>(status)
            : null;
    }

    public async Task<InstanceHistory?> HistoryOfAsync(Process process, object key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(key);
        await using var command = _dataSource.CreateCommand($"SELECT data FROM {_sql.S}.instances WHERE process = @p AND key = @k");
        command.Parameters.AddWithValue("p", process.Name);
        command.Parameters.AddWithValue("k", Keys.Of(key));
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is string data
            && JsonSerializer.Deserialize(data, WyrdJsonContext.Default.StoredInstance) is { } instance
            ? new InstanceHistory(instance.Process, instance.Key, instance.Fingerprint, instance.History)
            : null;
    }

    public async Task<TState> StateOfAsync<TState>(object key, CancellationToken cancellationToken = default)
        where TState : IState<TState>
    {
        ArgumentNullException.ThrowIfNull(key);
        await using var command = _dataSource.CreateCommand($"SELECT type, payload FROM {_sql.S}.states WHERE state_type = @t AND key = @k");
        command.Parameters.AddWithValue("t", typeof(TState).FullName!);
        command.Parameters.AddWithValue("k", Keys.Of(key));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? (TState)_application.Types.Deserialize(reader.GetString(0), reader.GetString(1))
            : TState.Initial;
    }

    public async Task<TView?> ViewAsync<TView>(object key, CancellationToken cancellationToken = default)
        where TView : class
    {
        ArgumentNullException.ThrowIfNull(key);
        await using var command = _dataSource.CreateCommand($"SELECT type, payload FROM {_sql.S}.views WHERE view_type = @v AND key = @k");
        command.Parameters.AddWithValue("v", typeof(TView).FullName!);
        command.Parameters.AddWithValue("k", Keys.Of(key));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? (TView)_application.Types.Deserialize(reader.GetString(0), reader.GetString(1))
            : null;
    }

    public async Task<IReadOnlyList<TView>> ViewsAsync<TView>(CancellationToken cancellationToken = default)
        where TView : class
    {
        await using var command = _dataSource.CreateCommand($"SELECT type, payload FROM {_sql.S}.views WHERE view_type = @v ORDER BY created");
        command.Parameters.AddWithValue("v", typeof(TView).FullName!);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var views = new List<TView>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            views.Add((TView)_application.Types.Deserialize(reader.GetString(0), reader.GetString(1)));
        }

        return views;
    }

    /// <summary>Messages waiting in the queue (due now or later) and messages that gave up (dead letters).</summary>
    public async Task<(long Queued, long DeadLetters)> BacklogAsync(CancellationToken cancellationToken = default)
    {
        await using var command = _dataSource.CreateCommand(
            $"SELECT (SELECT count(*) FROM {_sql.S}.queue), (SELECT count(*) FROM {_sql.S}.dead_letters)");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        return (reader.GetInt64(0), reader.GetInt64(1));
    }

    // ── Delivery ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Takes the next due message and handles it in the same transaction that removes it from the queue. Returns
    /// false when nothing is due. A failure rolls everything back and schedules a redelivery.
    /// </summary>
    private async Task<bool> ProcessOneAsync(CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        long id;
        int attempts;
        Envelope envelope;
        await using (var claim = new NpgsqlCommand(
            $"SELECT id, envelope, attempts FROM {_sql.S}.queue WHERE available_at <= @now ORDER BY available_at, id LIMIT 1 FOR UPDATE SKIP LOCKED",
            connection,
            transaction))
        {
            claim.Parameters.AddWithValue("now", now);
            await using var reader = await claim.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return false;
            }

            id = reader.GetInt64(0);
            envelope = JsonSerializer.Deserialize(reader.GetString(1), WyrdJsonContext.Default.Envelope)!;
            attempts = reader.GetInt32(2);
        }

        try
        {
            var store = new PostgresStore(connection, transaction, _sql, now);
            await Engine(store, now).HandleAsync(envelope, cancellationToken).ConfigureAwait(false);
            await using (var done = new NpgsqlCommand($"DELETE FROM {_sql.S}.queue WHERE id = @id", connection, transaction))
            {
                done.Parameters.AddWithValue("id", id);
                await done.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            await RedeliverLaterAsync(connection, id, attempts, ex, now).ConfigureAwait(false);
        }

        return true;
    }

    private async Task RedeliverLaterAsync(NpgsqlConnection connection, long id, int attempts, Exception error, DateTimeOffset now)
    {
        // A concurrency conflict is normal between workers: try again right away, without counting it as a failure.
        if (IsConflict(error))
        {
            LogConflict(id);
            return;
        }

        var failed = attempts + 1;
        if (failed >= _options.MaxDeliveryAttempts)
        {
            LogDeadLetter(id, failed, error);
            await using var dead = new NpgsqlCommand(
                $"""
                INSERT INTO {_sql.S}.dead_letters (id, envelope, attempts, error, failed_at)
                SELECT id, envelope, @attempts, @error, @now FROM {_sql.S}.queue WHERE id = @id;
                DELETE FROM {_sql.S}.queue WHERE id = @id;
                """,
                connection);
            dead.Parameters.AddWithValue("id", id);
            dead.Parameters.AddWithValue("attempts", failed);
            dead.Parameters.AddWithValue("error", error.ToString());
            dead.Parameters.AddWithValue("now", now);
            await dead.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
            return;
        }

        var delay = TimeSpan.FromTicks(Math.Min(_options.RedeliveryDelay.Ticks * (1L << Math.Min(failed - 1, 20)), TimeSpan.FromMinutes(1).Ticks));
        LogRedelivery(id, failed, delay, error);
        await using var retry = new NpgsqlCommand(
            $"UPDATE {_sql.S}.queue SET attempts = @attempts, available_at = @at, last_error = @error WHERE id = @id",
            connection);
        retry.Parameters.AddWithValue("id", id);
        retry.Parameters.AddWithValue("attempts", failed);
        retry.Parameters.AddWithValue("at", now + delay);
        retry.Parameters.AddWithValue("error", error.Message);
        await retry.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private async Task<T> InTransactionAsync<T>(Func<Engine, PostgresStore, ValueTask<T>> work, CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var store = new PostgresStore(connection, transaction, _sql, now);
        var result = await work(Engine(store, now), store).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    private Engine Engine(PostgresStore store, DateTimeOffset now) =>
        new(_application.Registry, store, _application.Types, () => now, Guid.CreateVersion7, static (_, _) => false, LogStep);

    private static bool IsConflict(Exception ex) =>
        ex is ConcurrencyConflictException or PostgresException { SqlState: PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.SerializationFailure };

    private void LogStep(string process, string? key, string message) => LogJournal(process, key ?? "", message);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{Process} {Key} {Message}")]
    private partial void LogJournal(string process, string key, string message);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Concurrency conflict on message {Id}: retrying")]
    private partial void LogConflict(long id);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Concurrency conflict sending {Command} (attempt {Attempt}): retrying")]
    private partial void LogConflictRetry(string command, int attempt);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Message {Id} failed (attempt {Attempt}), redelivery in {Delay}")]
    private partial void LogRedelivery(long id, int attempt, TimeSpan delay, Exception error);

    [LoggerMessage(Level = LogLevel.Error, Message = "Message {Id} moved to the dead letters after {Attempts} attempts")]
    private partial void LogDeadLetter(long id, int attempts, Exception error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Database unavailable; queued messages are safe, retrying")]
    private partial void LogDatabaseUnavailable(Exception error);
}
