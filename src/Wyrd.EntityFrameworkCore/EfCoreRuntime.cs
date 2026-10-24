using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
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
public sealed partial class EfCoreRuntime<TContext> : IWyrdBuilder<EfCoreRuntime<TContext>>, IWyrdRuntime, IDisposable
    where TContext : DbContext
{
    private readonly IDbContextFactory<TContext> _contexts;
    private readonly Application _application;
    private readonly EfCoreRuntimeOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private Dialect? _dialect;
    private readonly Retention _retention;
    private IWyrdTransport? _transport;
    private IHistoryArchive? _archive;
    private DateTimeOffset _lastCleanup = DateTimeOffset.MinValue;
    private DateTimeOffset _lastHealth = DateTimeOffset.MinValue;
    private readonly SemaphoreSlim _work = new(0, 1);

    /// <summary>
    /// Source-generated JSON from your <c>[WyrdJsonContext]</c>. <paramref name="retention"/> says how long each kind of
    /// data is kept: there is no default, it is your decision.
    /// </summary>
    public EfCoreRuntime(
        IDbContextFactory<TContext> contexts,
        JsonSerializerContext json,
        Retention retention,
        EfCoreRuntimeOptions? options = null,
        TimeProvider? time = null,
        ILogger<EfCoreRuntime<TContext>>? logger = null)
        : this(contexts, MessageTypes.FromContext(json ?? throw new ArgumentNullException(nameof(json))), retention, options, time, logger)
    {
    }

    /// <summary>Reflection-based JSON, for development. Use a <c>[WyrdJsonContext]</c> in production.</summary>
    [RequiresUnreferencedCode("Reflection-based System.Text.Json. Pass a [WyrdJsonContext] for trimming.")]
    [RequiresDynamicCode("Reflection-based System.Text.Json. Pass a [WyrdJsonContext] for trimming.")]
    public EfCoreRuntime(
        IDbContextFactory<TContext> contexts,
        Retention retention,
        EfCoreRuntimeOptions? options = null,
        TimeProvider? time = null,
        ILogger<EfCoreRuntime<TContext>>? logger = null)
        : this(contexts, MessageTypes.Reflection(), retention, options, time, logger)
    {
    }

    private EfCoreRuntime(
        IDbContextFactory<TContext> contexts, MessageTypes types, Retention retention, EfCoreRuntimeOptions? options, TimeProvider? time, ILogger? logger)
    {
        _contexts = contexts ?? throw new ArgumentNullException(nameof(contexts));
        _retention = retention ?? throw new ArgumentNullException(nameof(retention));
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

    /// <inheritdoc />
    public EfCoreRuntime<TContext> AddProcess(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        _application.Add(process);
        return this;
    }

    /// <summary>
    /// Hands the histories of finished instances to <paramref name="archive"/> before retention removes them, outside any
    /// transaction; an instance is removed only once its history is archived.
    /// </summary>
    public EfCoreRuntime<TContext> UseHistoryArchive(IHistoryArchive archive)
    {
        ArgumentNullException.ThrowIfNull(archive);
        _archive = archive;
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
                Wake();
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
    /// tables (<see cref="WyrdModelBuilderExtensions.AddWyrd"/>). A concurrency conflict surfaces as a
    /// <see cref="ConcurrencyConflictException"/> for you to retry, since the transaction is yours.
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

        try
        {
            var decision = await HandleAsync(context, command, cancellationToken).ConfigureAwait(false);
            Wake(); // early, before your commit: a worker that finds nothing yet goes back to waiting
            return decision;
        }
        catch (Exception ex) when (ex is not ConcurrencyConflictException && IsConflict(ex))
        {
            throw new ConcurrencyConflictException($"Another transaction changed what {command.GetType().Name} decides on first: roll back and retry.", ex);
        }
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
        Wake();
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
        Wake();
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
        var idle = _options.PollInterval;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var handled = await ProcessPendingAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
                var relayed = await RelayOrWaitAsync(cancellationToken).ConfigureAwait(false);
                if (_application.Registry.CatchingUp.Any())
                {
                    await CatchUpAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
                }

                if (_time.GetUtcNow() - _lastCleanup >= _options.CleanupInterval)
                {
                    _lastCleanup = _time.GetUtcNow();
                    await ForgetExpiredAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
                }

                if (_time.GetUtcNow() - _lastHealth >= _options.HealthInterval)
                {
                    _lastHealth = _time.GetUtcNow();
                    QueueMetrics.Record(QueueName(), await HealthAsync(cancellationToken).ConfigureAwait(false));
                }

                if (handled == 0 && relayed == 0)
                {
                    await IdleAsync(idle, cancellationToken).ConfigureAwait(false);
                    idle = TimeSpan.FromTicks(Math.Min(idle.Ticks * 2, _options.MaxPollInterval.Ticks));
                }
                else
                {
                    idle = _options.PollInterval;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // The database is unreachable, or recording a failure failed: wait and try again. Every change is
                // transactional, so queued messages are safe and nothing is half done.
                LogWorkerFailed(ex);
                await Task.Delay(_options.PollInterval, _time, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Waits <paramref name="delay"/>, or less if this runtime queues a message meanwhile. The signal stays set until a
    /// worker takes it, so a message queued while the worker was looking still ends the next wait at once.
    /// </summary>
    private async Task IdleAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(delay, _time);
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            await _work.WaitAsync(wait.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The wait is over: look again.
        }
    }

    /// <summary>Something was queued: an idle worker looks now instead of after its wait.</summary>
    private void Wake()
    {
        if (_work.CurrentCount == 0)
        {
            try
            {
                _work.Release();
            }
            catch (SemaphoreFullException)
            {
                // Another caller set the signal first.
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
            QueueMetrics.Handled.Add(1);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            await RedeliverLaterAsync(message, ex, now).ConfigureAwait(false);
        }

        return true;
    }

    /// <summary>
    /// Catches up the projections added to a store that already has states (see
    /// <see cref="Projection{TView}.Seed{TState}"/>): seeds up to <paramref name="batchSize"/> views of each, one
    /// transaction per state, and marks a projection caught up once nothing is left. Workers call it while a projection is
    /// catching up, and any number of them can: a state another worker seeded meanwhile is skipped. Returns true once
    /// every projection has caught up.
    /// </summary>
    public async Task<bool> CatchUpAsync(int batchSize = 1_000, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);
        foreach (var projection in _application.Registry.CatchingUp.ToList())
        {
            foreach (var seed in projection.Seeds)
            {
                IReadOnlyList<StoredState> states;
                await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
                {
                    states = await new EfCoreStore(db, _time.GetUtcNow())
                        .UnseededAsync(projection.View.FullName!, seed.FullName!, batchSize, cancellationToken).ConfigureAwait(false);
                }

                foreach (var state in states)
                {
                    await CatchUpStepAsync(engine => engine.SeedAsync(projection, state, cancellationToken), cancellationToken).ConfigureAwait(false);
                }
            }

            await CatchUpStepAsync(async engine => await engine.CompleteCatchUpAsync(projection, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
        }

        return !_application.Registry.CatchingUp.Any();
    }

    /// <summary>One step of a catch-up in its own transaction; a conflict leaves it for the next pass.</summary>
    private async Task CatchUpStepAsync(Func<Engine, ValueTask> step, CancellationToken cancellationToken)
    {
        try
        {
            await using var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await step(Engine(db, _time.GetUtcNow())).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsConflict(ex))
        {
            LogConflictRetry("a catch-up", 1);
        }
    }

    /// <summary>
    /// Removes what the retention no longer keeps (Q2.3), in batches of <paramref name="batchSize"/> so no transaction
    /// holds many locks; returns how many rows went. Suspended and running instances stay; a removed instance takes its
    /// parked events with it. Workers call it every <see cref="EfCoreRuntimeOptions.CleanupInterval"/>.
    /// </summary>
    public async Task<int> ForgetExpiredAsync(int batchSize = 1_000, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);
        var now = _time.GetUtcNow();
        var removed = 0;
        if (_retention.Deduplication.Cutoff(now) is { } dedup)
        {
            // A fence blocks its command forever (semantics 3.4): it never expires.
            removed += await ForgetAsync<InboxRow, Guid>(r => r.RecordedAt <= dedup && !r.Fence, r => r.MessageId, batchSize, cancellationToken).ConfigureAwait(false);
            removed += await ForgetAsync<HandledRow, Guid>(r => r.HandledAt <= dedup, r => r.MessageId, batchSize, cancellationToken).ConfigureAwait(false);
        }

        if (_retention.FinishedInstances.Cutoff(now) is { } finished)
        {
            removed += await ForgetFinishedInstancesAsync(finished, batchSize, cancellationToken).ConfigureAwait(false);
        }

        if (_retention.RelayedMessages.Cutoff(now) is { } relayed)
        {
            // Without a transport nothing leaves: a published message expires from when it was published.
            removed += _transport is null
                ? await ForgetAsync<PublishedRow, long>(r => r.RecordedAt <= relayed, r => r.Seq, batchSize, cancellationToken).ConfigureAwait(false)
                : await ForgetAsync<PublishedRow, long>(r => r.RelayedAt <= relayed, r => r.Seq, batchSize, cancellationToken).ConfigureAwait(false);
        }

        if (_retention.Events.Cutoff(now) is { } events)
        {
            removed += await ForgetAsync<EventRow, long>(r => r.RecordedAt <= events, r => r.Seq, batchSize, cancellationToken).ConfigureAwait(false);
        }

        // A seed mark written by a worker while another was marking its projection caught up: no longer read. A handful
        // at most, from a race, so no batches.
        await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            removed += await db.Set<SeededRow>()
                .Where(s => db.Set<CaughtUpRow>().Any(c => c.ViewType == s.ViewType))
                .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        }

        if (_retention.DeadLetters.Cutoff(now) is { } dead)
        {
            removed += await ForgetAsync<DeadLetterRow, long>(r => r.FailedAt <= dead, r => r.Id, batchSize, cancellationToken).ConfigureAwait(false);
        }

        return removed;
    }

    private async Task<int> ForgetAsync<TRow, TKey>(
        Expression<Func<TRow, bool>> expired, Expression<Func<TRow, TKey>> key, int batchSize, CancellationToken cancellationToken)
        where TRow : class
    {
        var removed = 0;
        int batch;
        do
        {
            await using var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            batch = await db.Set<TRow>().Where(expired).OrderBy(key).Take(batchSize).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            removed += batch;
        }
        while (batch == batchSize);

        return removed;
    }

    private async Task<int> ForgetFinishedInstancesAsync(DateTimeOffset cutoff, int batchSize, CancellationToken cancellationToken)
    {
        var removed = 0;
        while (true)
        {
            List<InstanceRow> finished;
            await using (var reader = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                var expired = reader.Set<InstanceRow>().AsNoTracking().Where(r => r.FinishedAt <= cutoff).OrderBy(r => r.FinishedAt).Take(batchSize);
                finished = _archive is null
                    ? await expired.Select(r => new InstanceRow { Process = r.Process, Key = r.Key }).ToListAsync(cancellationToken).ConfigureAwait(false)
                    : await expired.ToListAsync(cancellationToken).ConfigureAwait(false);
            }

            if (finished.Count == 0)
            {
                return removed;
            }

            if (_archive is not null)
            {
                // Archived before anything is removed, with no transaction open: a failed archive keeps them for the next pass.
                try
                {
                    await _archive.ArchiveAsync([.. finished.Select(HistoryOf)], cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogArchiveFailed(finished.Count, ex);
                    return removed;
                }
            }

            await using var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            foreach (var instance in finished)
            {
                // Checked again in the delete: an instance can't be running again, but its row is only removed while finished.
                removed += await db.Set<InstanceRow>()
                    .Where(r => r.Process == instance.Process && r.Key == instance.Key && r.FinishedAt <= cutoff)
                    .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
                await db.Set<ParkedRow>().Where(r => r.Process == instance.Process && r.Key == instance.Key)
                    .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
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

        QueueMetrics.Failed.Add(1);

        // The rollback released the row lock, so another worker may have claimed the message meanwhile. Every branch
        // first takes the row back, unchanged since this attempt, and writes the failure only when that succeeded; if
        // the other worker still holds it, the statement waits for its commit and then finds nothing.
        await using var db = await _contexts.CreateDbContextAsync(CancellationToken.None).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(CancellationToken.None).ConfigureAwait(false);
        var unchanged = db.Set<QueueRow>().Where(r => r.Id == message.Id && r.Attempts == message.Attempts);

        // One retry layer: a process command that can't be handled goes back to its process as a failure, and the
        // step's retry policy decides. The host never retries it on its own (semantics 3.3).
        var envelope = JsonSerializer.Deserialize(message.Envelope, WyrdJsonContext.Default.Envelope)!;
        var failed = message.Attempts + 1;
        if (envelope is { Kind: MessageKind.Command, Process: not null })
        {
            if (await unchanged.ExecuteDeleteAsync(CancellationToken.None).ConfigureAwait(false) == 0)
            {
                LogTakenMeanwhile(message.Id);
                return;
            }

            LogProcessCommandFailed(message.Id, envelope.Process.Process, envelope.Process.Key, error);
            new EfCoreStore(db, now).Enqueue(Wyrd.Runtime.Engine.Faulted(envelope, Guid.CreateVersion7()), now);
            await db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        }
        else if (failed >= _options.MaxDeliveryAttempts)
        {
            if (await unchanged.ExecuteDeleteAsync(CancellationToken.None).ConfigureAwait(false) == 0)
            {
                LogTakenMeanwhile(message.Id);
                return;
            }

            LogDeadLetter(message.Id, failed, error);
            QueueMetrics.DeadLettered.Add(1);
            db.Add(new DeadLetterRow { Id = message.Id, Envelope = message.Envelope, Attempts = failed, Error = error.ToString(), FailedAt = now });
            await db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        }
        else
        {
            var delay = TimeSpan.FromTicks(Math.Min(_options.RedeliveryDelay.Ticks * (1L << Math.Min(failed - 1, 20)), TimeSpan.FromMinutes(1).Ticks));
            if (await unchanged.ExecuteUpdateAsync(
                    s => s.SetProperty(r => r.Attempts, failed).SetProperty(r => r.AvailableAt, now + delay).SetProperty(r => r.LastError, error.Message),
                    CancellationToken.None).ConfigureAwait(false) == 0)
            {
                LogTakenMeanwhile(message.Id);
                return;
            }

            LogRedelivery(message.Id, failed, delay, error);
        }

        await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
    }

    // ── Operators ────────────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public Task ResumeAsync(Process process, object key, CancellationToken cancellationToken = default) =>
        OperateAsync(process, key, (engine, name, k) => engine.ResumeAsync(name, k, cancellationToken), cancellationToken);

    /// <inheritdoc />
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
        Wake();
    }

    /// <summary>Releases the workers' wake-up signal. Stop the workers (<see cref="RunAsync"/>) first.</summary>
    public void Dispose() => _work.Dispose();

    // ── Queries ──────────────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
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

    /// <inheritdoc />
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

        return HistoryOf(row);
    }

    private static InstanceHistory HistoryOf(InstanceRow row)
    {
        var instance = EfCoreStore.Deserialize(row);
        return new InstanceHistory(instance.Process, instance.Key, instance.Fingerprint, instance.History);
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <summary>
    /// Measures the queue (<see cref="QueueHealth"/>): its lag, what waits for its time, dead letters, pending steps and
    /// suspended instances. Workers publish it on the <c>Wyrd</c> meter every <see cref="EfCoreRuntimeOptions.HealthInterval"/>.
    /// </summary>
    public async Task<QueueHealth> HealthAsync(CancellationToken cancellationToken = default)
    {
        var now = _time.GetUtcNow();
        await using var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var due = db.Set<QueueRow>().Where(r => r.AvailableAt <= now);
        var pending = db.Set<InstanceRow>().Where(r => r.PendingSince != null);
        return new QueueHealth(
            Due: await due.LongCountAsync(cancellationToken).ConfigureAwait(false),
            OldestDue: Age(await due.MinAsync(r => (DateTimeOffset?)r.AvailableAt, cancellationToken).ConfigureAwait(false), now),
            Scheduled: await db.Set<QueueRow>().LongCountAsync(r => r.AvailableAt > now, cancellationToken).ConfigureAwait(false),
            DeadLetters: await db.Set<DeadLetterRow>().LongCountAsync(cancellationToken).ConfigureAwait(false),
            PendingSteps: await pending.LongCountAsync(cancellationToken).ConfigureAwait(false),
            OldestPendingStep: Age(await pending.MinAsync(r => r.PendingSince, cancellationToken).ConfigureAwait(false), now),
            // A literal, so the filtered index ix_instances_suspended answers it.
            Suspended: await db.Set<InstanceRow>().LongCountAsync(r => r.Status == "Suspended", cancellationToken).ConfigureAwait(false));

        static TimeSpan Age(DateTimeOffset? since, DateTimeOffset now) => since < now ? now - since.Value : TimeSpan.Zero;
    }

    private string QueueName()
    {
        using var db = _contexts.CreateDbContext();
        var queue = db.Model.FindEntityType(typeof(QueueRow))!;
        return $"{queue.GetSchema()}.{queue.GetTableName()}";
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Worker failed (database unavailable?); queued messages are safe, retrying")]
    private partial void LogWorkerFailed(Exception error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "History archive failed: {Count} finished instances kept for the next cleanup")]
    private partial void LogArchiveFailed(int count, Exception error);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Message {Id} was taken by another worker meanwhile: its failure is not recorded twice")]
    private partial void LogTakenMeanwhile(long id);
}
