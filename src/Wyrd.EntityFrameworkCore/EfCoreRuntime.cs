using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Wyrd.Processes;
using Wyrd.Runtime;

namespace Wyrd.EntityFrameworkCore;

/// <summary>
/// Verdandi on EF Core: the durable Wyrd runtime for PostgreSQL and SQL Server. Every message is handled in one
/// transaction that also takes it off the queue: state, events, inbox, outbox, timers and read models commit together,
/// or not at all. The queue is a table, so any number of workers — in one process or many — share it, and nothing is lost
/// on a crash or a restart. Wyrd's tables live in <typeparamref name="TContext"/>: your own context (see
/// <see cref="WyrdModelBuilderExtensions.AddWyrd"/>) or <see cref="WyrdDbContext"/>.
/// </summary>
public sealed partial class EfCoreRuntime<TContext> : IWyrdBuilder<EfCoreRuntime<TContext>>, IWyrdRuntime
    where TContext : DbContext
{
    private readonly IDbContextFactory<TContext> _contexts;
    private readonly Application _application;
    private readonly EfCoreRuntimeOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private Dialect? _dialect;
    private IWyrdTransport? _transport;

    /// <summary>Source-generated JSON from your <c>[WyrdJsonContext]</c>.</summary>
    public EfCoreRuntime(
        IDbContextFactory<TContext> contexts,
        JsonSerializerContext json,
        EfCoreRuntimeOptions? options = null,
        TimeProvider? time = null,
        ILogger<EfCoreRuntime<TContext>>? logger = null)
        : this(contexts, MessageTypes.FromContext(json ?? throw new ArgumentNullException(nameof(json))), options, time, logger)
    {
    }

    /// <summary>Reflection-based JSON, for development. Use a <c>[WyrdJsonContext]</c> in production.</summary>
    [RequiresUnreferencedCode("Reflection-based System.Text.Json. Pass a [WyrdJsonContext] for trimming.")]
    [RequiresDynamicCode("Reflection-based System.Text.Json. Pass a [WyrdJsonContext] for trimming.")]
    public EfCoreRuntime(
        IDbContextFactory<TContext> contexts,
        EfCoreRuntimeOptions? options = null,
        TimeProvider? time = null,
        ILogger<EfCoreRuntime<TContext>>? logger = null)
        : this(contexts, MessageTypes.Reflection(), options, time, logger)
    {
    }

    private EfCoreRuntime(
        IDbContextFactory<TContext> contexts, MessageTypes types, EfCoreRuntimeOptions? options, TimeProvider? time, ILogger? logger)
    {
        _contexts = contexts ?? throw new ArgumentNullException(nameof(contexts));
        _application = new Application(types);
        _options = options ?? new EfCoreRuntimeOptions();
        _time = time ?? TimeProvider.System;
        _logger = logger ?? NullLogger.Instance;
    }

    // ── Registration ─────────────────────────────────────────────────────────────────────────

    /// <summary>Registers the integration that owns a command carried out by another system (the only I/O).</summary>
    public EfCoreRuntime<TContext> AddIntegration<TCommand, TEvent, TError>(Integration<TCommand, TEvent, TError> integration)
        where TCommand : ICommand
        where TEvent : IDomainEvent
        where TError : notnull
    {
        ArgumentNullException.ThrowIfNull(integration);
        _application.Add(integration);
        return this;
    }

    /// <summary>Registers the single owner of a command type.</summary>
    public EfCoreRuntime<TContext> AddSlice<TCommand, TState, TEvent, TError>(Slice<TCommand, TState, TEvent, TError> slice)
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
    public EfCoreRuntime<TContext> AddPublication<TDomainEvent, TIntegrationEvent>(Publication<TDomainEvent, TIntegrationEvent> publication)
        where TDomainEvent : IDomainEvent
        where TIntegrationEvent : IIntegrationEvent
    {
        ArgumentNullException.ThrowIfNull(publication);
        _application.Add(publication);
        return this;
    }

    /// <summary>Registers an upcaster from one contract version to the next.</summary>
    public EfCoreRuntime<TContext> AddUpcaster<TFrom, TTo>(Upcaster<TFrom, TTo> upcaster)
        where TFrom : IIntegrationEvent
        where TTo : IIntegrationEvent
    {
        ArgumentNullException.ThrowIfNull(upcaster);
        _application.Add(upcaster);
        return this;
    }

    /// <summary>Registers the projection that owns a read model.</summary>
    public EfCoreRuntime<TContext> AddProjection<TView>(Projection<TView> projection)
        where TView : class
    {
        ArgumentNullException.ThrowIfNull(projection);
        _application.Add(projection);
        return this;
    }

    public EfCoreRuntime<TContext> AddProcess(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        _application.Add(process);
        return this;
    }

    /// <summary>
    /// Sends what this service publishes (integration events) to other services through <paramref name="transport"/>:
    /// the workers relay the outbox after each commit, at least once.
    /// </summary>
    public EfCoreRuntime<TContext> UseTransport(IWyrdTransport transport)
    {
        ArgumentNullException.ThrowIfNull(transport);
        _transport = transport;
        return this;
    }

    // ── Commands and facts ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Executes a command coming from outside (an API call, a UI action...) in its own transaction and returns the
    /// decision. What it causes — events, process steps — is queued and handled by the workers.
    /// </summary>
    public async Task<Decision> SendAsync(ICommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
                await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
                var decision = await HandleAsync(db, command, cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return decision;
            }
            catch (Exception ex) when (attempt < _options.MaxConflictRetries && IsConflict(ex))
            {
                LogConflictRetry(command.GetType().Name, attempt);
            }
        }
    }

    /// <summary>
    /// Executes a command inside <paramref name="context"/>'s current transaction, which you begin and commit: your own
    /// changes in that context and Wyrd's are saved together, or not at all. The context's model must include Wyrd's
    /// tables (<see cref="WyrdModelBuilderExtensions.AddWyrd"/>). A concurrency conflict surfaces as an exception for
    /// you to retry, since the transaction is yours.
    /// </summary>
    public async Task<Decision> SendAsync(ICommand command, TContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);
        if (context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "Begin a transaction on the context first (context.Database.BeginTransactionAsync()): Wyrd joins it and you commit it.");
        }

        return await HandleAsync(context, command, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Delivers a fact from another context (a webhook, a message from another service...).</summary>
    public async Task PublishAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var (type, payload) = _application.Types.Serialize(integrationEvent, integrationEvent.GetType());
        await using var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var now = _time.GetUtcNow();
        new EfCoreStore(db, now).Enqueue(new Envelope(Guid.CreateVersion7(), MessageKind.Event, type, payload, Trace: Tracing.Capture()), now);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Queues an integration event another service published (from a transport), keeping its message id: a redelivery
    /// is handled once. A contract nobody uses here is ignored. Acknowledge the message to the broker after this returns.
    /// </summary>
    public async Task ReceiveAsync(TransportMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (!_application.Uses(message))
        {
            LogIgnored(message.Type, message.MessageId);
            return;
        }

        await using var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var now = _time.GetUtcNow();
        new EfCoreStore(db, now).Enqueue(
            new Envelope(message.MessageId, MessageKind.Event, message.Type, message.Payload, Trace: Tracing.Received(message)), now);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<Decision> HandleAsync(TContext db, ICommand command, CancellationToken cancellationToken)
    {
        var (type, payload) = _application.Types.Serialize(command, command.GetType());
        var envelope = new Envelope(Guid.CreateVersion7(), MessageKind.Command, type, payload);
        var now = _time.GetUtcNow();
        return (await Engine(db, now).HandleAsync(envelope, cancellationToken).ConfigureAwait(false))!;
    }

    // ── Workers ──────────────────────────────────────────────────────────────────────────────

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

    /// <summary>
    /// The outbox relay: hands published integration events to the transport, oldest first, in batches, and marks them
    /// relayed. Relays in other workers skip the rows this one holds. A message sent but not yet marked when the process
    /// stops is sent again: the receiver discards the duplicate. Returns how many were relayed.
    /// </summary>
    public async Task<int> RelayPendingAsync(int max = 1_000, CancellationToken cancellationToken = default)
    {
        if (_transport is null)
        {
            return 0;
        }

        var relayed = 0;
        while (relayed < max)
        {
            var batch = await RelayBatchAsync(Math.Min(_options.RelayBatchSize, max - relayed), cancellationToken).ConfigureAwait(false);
            if (batch == 0)
            {
                break;
            }

            relayed += batch;
        }

        return relayed;
    }

    private async Task<int> RelayBatchAsync(int size, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await TryRelayBatchAsync(size, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (attempt < _options.MaxConflictRetries && IsConflict(ex))
            {
                // Lost a race with another relay (a deadlock victim on SQL Server): its transaction rolled back, so
                // nothing it claimed stays marked, and claiming again is safe.
                LogConflictRetry("the relay", attempt);
            }
        }
    }

    private async Task<int> TryRelayBatchAsync(int size, CancellationToken cancellationToken)
    {
        await using var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var dialect = DialectOf(db);
        var published = db.Model.FindEntityType(typeof(PublishedRow))!;
        var claimed = await db.Set<PublishedRow>()
            .FromSqlRaw(dialect.ClaimPublished(dialect.Table(published.GetSchema()!, published.GetTableName()!)), size, _time.GetUtcNow())
            .AsNoTracking()
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        claimed.Sort((a, b) => a.Seq.CompareTo(b.Seq));

        for (var sent = 0; sent < claimed.Count; sent++)
        {
            try
            {
                var row = claimed[sent];
                await _transport!.SendAsync(
                    new TransportMessage(row.MessageId, row.Type, row.Payload) { TraceParent = row.TraceParent, TraceState = row.TraceState },
                    cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // What the transport took stays relayed; the rest waits for the next relay.
                var unsent = claimed.Skip(sent).Select(r => r.Seq).ToList();
                await db.Set<PublishedRow>().Where(r => unsent.Contains(r.Seq))
                    .ExecuteUpdateAsync(s => s.SetProperty(r => r.RelayedAt, (DateTimeOffset?)null), CancellationToken.None).ConfigureAwait(false);
                await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
                throw;
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return claimed.Count;
    }

    /// <summary>
    /// A worker: handles messages as they become due and relays what was published, until cancelled. Run as many as you
    /// like.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var handled = await ProcessPendingAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
                var relayed = await RelayOrWaitAsync(cancellationToken).ConfigureAwait(false);
                if (handled == 0 && relayed == 0)
                {
                    await Task.Delay(_options.PollInterval, _time, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (DbException ex)
            {
                // The database is unreachable: wait and try again; queued messages are safe.
                LogDatabaseUnavailable(ex);
                await Task.Delay(_options.PollInterval, _time, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Takes the next due message and handles it in the same transaction that removes it from the queue. Returns false
    /// when nothing is due. A failure rolls everything back and schedules a redelivery.
    /// </summary>
    private async Task<bool> ProcessOneAsync(CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();
        await using var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var dialect = DialectOf(db);
        var queue = db.Model.FindEntityType(typeof(QueueRow))!;
        var claimed = await db.Set<QueueRow>()
            .FromSqlRaw(dialect.Dequeue(dialect.Table(queue.GetSchema()!, queue.GetTableName()!)), now)
            .AsNoTracking()
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        if (claimed is not [var message])
        {
            return false;
        }

        try
        {
            var envelope = JsonSerializer.Deserialize(message.Envelope, WyrdJsonContext.Default.Envelope)!;
            await Engine(db, now).HandleAsync(envelope, cancellationToken).ConfigureAwait(false);
            await db.Set<QueueRow>().Where(r => r.Id == message.Id).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            await RedeliverLaterAsync(message, ex, now).ConfigureAwait(false);
        }

        return true;
    }

    /// <summary>A transport that is down doesn't stop the worker: the outbox keeps the messages until it is back.</summary>
    private async Task<int> RelayOrWaitAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await RelayPendingAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not DbException)
        {
            LogTransportUnavailable(ex);
            return 0;
        }
    }

    private async Task RedeliverLaterAsync(QueueRow message, Exception error, DateTimeOffset now)
    {
        // A concurrency conflict is normal between workers: try again right away, without counting it as a failure.
        if (IsConflict(error))
        {
            LogConflict(message.Id);
            return;
        }

        await using var db = await _contexts.CreateDbContextAsync(CancellationToken.None).ConfigureAwait(false);

        // One retry layer: a process command that can't be handled goes back to its process as a failure, and the
        // step's retry policy decides. The host never retries it on its own (semantics 3.3).
        var envelope = JsonSerializer.Deserialize(message.Envelope, WyrdJsonContext.Default.Envelope)!;
        if (envelope is { Kind: MessageKind.Command, Process: not null })
        {
            LogProcessCommandFailed(message.Id, envelope.Process.Process, envelope.Process.Key, error);
            await using var transaction = await db.Database.BeginTransactionAsync(CancellationToken.None).ConfigureAwait(false);
            new EfCoreStore(db, now).Enqueue(Wyrd.Runtime.Engine.Faulted(envelope, Guid.CreateVersion7()), now);
            await db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            await db.Set<QueueRow>().Where(r => r.Id == message.Id).ExecuteDeleteAsync(CancellationToken.None).ConfigureAwait(false);
            await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
            return;
        }

        var failed = message.Attempts + 1;
        if (failed >= _options.MaxDeliveryAttempts)
        {
            LogDeadLetter(message.Id, failed, error);
            await using var transaction = await db.Database.BeginTransactionAsync(CancellationToken.None).ConfigureAwait(false);
            db.Add(new DeadLetterRow { Id = message.Id, Envelope = message.Envelope, Attempts = failed, Error = error.ToString(), FailedAt = now });
            await db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            await db.Set<QueueRow>().Where(r => r.Id == message.Id).ExecuteDeleteAsync(CancellationToken.None).ConfigureAwait(false);
            await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
            return;
        }

        var delay = TimeSpan.FromTicks(Math.Min(_options.RedeliveryDelay.Ticks * (1L << Math.Min(failed - 1, 20)), TimeSpan.FromMinutes(1).Ticks));
        LogRedelivery(message.Id, failed, delay, error);
        await db.Set<QueueRow>().Where(r => r.Id == message.Id).ExecuteUpdateAsync(
            s => s.SetProperty(r => r.Attempts, failed).SetProperty(r => r.AvailableAt, now + delay).SetProperty(r => r.LastError, error.Message),
            CancellationToken.None).ConfigureAwait(false);
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
        await using var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await operation(Engine(db, _time.GetUtcNow()), process.Name, Keys.Of(key)).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    // ── Queries ──────────────────────────────────────────────────────────────────────────────

    public async Task<ProcessStatus?> StatusOfAsync(Process process, object key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(key);
        var name = Keys.Of(key);
        await using var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var status = await db.Set<InstanceRow>().AsNoTracking()
            .Where(r => r.Process == process.Name && r.Key == name).Select(r => r.Status)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return status is null ? null : Enum.Parse<ProcessStatus>(status);
    }

    public async Task<InstanceHistory?> HistoryOfAsync(Process process, object key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(key);
        var name = Keys.Of(key);
        await using var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var row = await db.Set<InstanceRow>().AsNoTracking()
            .FirstOrDefaultAsync(r => r.Process == process.Name && r.Key == name, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            return null;
        }

        var instance = EfCoreStore.Deserialize(row);
        return new InstanceHistory(instance.Process, instance.Key, instance.Fingerprint, instance.History);
    }

    public async Task<TState> StateOfAsync<TState>(object key, CancellationToken cancellationToken = default)
        where TState : IState<TState>
    {
        ArgumentNullException.ThrowIfNull(key);
        var name = Keys.Of(key);
        var stateType = typeof(TState).FullName!;
        await using var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var row = await db.Set<StateRow>().AsNoTracking()
            .FirstOrDefaultAsync(r => r.StateType == stateType && r.Key == name, cancellationToken).ConfigureAwait(false);
        return row is null ? TState.Initial : (TState)_application.Types.Deserialize(row.Type, row.Payload);
    }

    public async Task<TView?> ViewAsync<TView>(object key, CancellationToken cancellationToken = default)
        where TView : class
    {
        ArgumentNullException.ThrowIfNull(key);
        var name = Keys.Of(key);
        var viewType = typeof(TView).FullName!;
        await using var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var row = await db.Set<ViewRow>().AsNoTracking()
            .FirstOrDefaultAsync(r => r.ViewType == viewType && r.Key == name, cancellationToken).ConfigureAwait(false);
        return row is null ? null : (TView)_application.Types.Deserialize(row.Type, row.Payload);
    }

    /// <summary>Every document of a read model, in creation order.</summary>
    public async Task<IReadOnlyList<TView>> ViewsAsync<TView>(CancellationToken cancellationToken = default)
        where TView : class
    {
        var viewType = typeof(TView).FullName!;
        await using var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var rows = await db.Set<ViewRow>().AsNoTracking()
            .Where(r => r.ViewType == viewType).OrderBy(r => r.Created).ThenBy(r => r.Key)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return [.. rows.Select(r => (TView)_application.Types.Deserialize(r.Type, r.Payload))];
    }

    /// <summary>Messages waiting in the queue (due now or later) and messages that gave up (dead letters).</summary>
    public async Task<(long Queued, long DeadLetters)> BacklogAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return (await db.Set<QueueRow>().LongCountAsync(cancellationToken).ConfigureAwait(false),
            await db.Set<DeadLetterRow>().LongCountAsync(cancellationToken).ConfigureAwait(false));
    }

    // ── Plumbing ─────────────────────────────────────────────────────────────────────────────

    private Engine Engine(DbContext db, DateTimeOffset now) =>
        new(_application.Registry, new EfCoreStore(db, now), _application.Types, () => now, Guid.CreateVersion7, static (_, _) => false, LogStep);

    private Dialect DialectOf(DbContext db) => _dialect ??= Dialect.For(db.Database);

    private bool IsConflict(Exception ex)
    {
        if (ex is ConcurrencyConflictException || ex.InnerException is ConcurrencyConflictException)
        {
            return true;
        }

        if (_dialect is null)
        {
            using var db = _contexts.CreateDbContext();
            DialectOf(db);
        }

        return _dialect!.IsConflict(ex);
    }

    private void LogStep(string process, string? key, string message) => LogJournal(process, key ?? "", message);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{Process} {Key} {Message}")]
    private partial void LogJournal(string process, string key, string message);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Concurrency conflict on message {Id}: retrying")]
    private partial void LogConflict(long id);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Concurrency conflict sending {Command} (attempt {Attempt}): retrying")]
    private partial void LogConflictRetry(string command, int attempt);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Message {Id} failed (attempt {Attempt}), redelivery in {Delay}")]
    private partial void LogRedelivery(long id, int attempt, TimeSpan delay, Exception error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Command {Id} of {Process} {Key} failed: its process decides whether to retry")]
    private partial void LogProcessCommandFailed(long id, string process, string key, Exception error);

    [LoggerMessage(Level = LogLevel.Error, Message = "Message {Id} moved to the dead letters after {Attempts} attempts")]
    private partial void LogDeadLetter(long id, int attempts, Exception error);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{Type} ({Id}) is not used here, ignored")]
    private partial void LogIgnored(string type, Guid id);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Transport unavailable; published messages wait in the outbox, retrying")]
    private partial void LogTransportUnavailable(Exception error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Database unavailable; queued messages are safe, retrying")]
    private partial void LogDatabaseUnavailable(Exception error);
}
