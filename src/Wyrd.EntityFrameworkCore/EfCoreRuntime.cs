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
    // UTC ticks of the last cleanup and health measurement: every worker loop shares them, and claims a turn atomically.
    private long _lastCleanup;
    private long _lastHealth;
    private string? _queueName;
    private volatile bool _disposed;
    private int _idleWaits;
    private int _catchingUp;
    private readonly SemaphoreSlim _work = new(0, 1);

    /// <summary>
    /// Source-generated JSON from your <c>[WyrdJsonContext]</c>. <paramref name="retention"/> says how long each kind of
    /// data is kept: there is no default, it is your decision.
    /// </summary>
    [SuppressMessage("ApiDesign", "RS0026", Justification = "The overloads ship together in the package's first release and never bind ambiguously.")]
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
    [SuppressMessage("ApiDesign", "RS0026", Justification = "The overloads ship together in the package's first release and never bind ambiguously.")]
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
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(_options.PollInterval, TimeSpan.Zero, "options.PollInterval");
        ArgumentOutOfRangeException.ThrowIfLessThan(_options.MaxPollInterval, _options.PollInterval, "options.MaxPollInterval");
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(_options.HealthInterval, TimeSpan.Zero, "options.HealthInterval");
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(_options.CleanupInterval, TimeSpan.Zero, "options.CleanupInterval");
        if (!Enum.IsDefined(_options.Cleanup))
        {
            throw new ArgumentOutOfRangeException("options.Cleanup", _options.Cleanup, "Unknown cleanup mode.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(_options.MaxDeliveryAttempts, 1, "options.MaxDeliveryAttempts");
        ArgumentOutOfRangeException.ThrowIfLessThan(_options.RelayBatchSize, 1, "options.RelayBatchSize");
        ArgumentOutOfRangeException.ThrowIfLessThan(_options.MaxConflictRetries, 1, "options.MaxConflictRetries");
        ArgumentOutOfRangeException.ThrowIfLessThan(_options.RedeliveryDelay, TimeSpan.Zero, "options.RedeliveryDelay");
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(_options.IntegrationLease, TimeSpan.Zero, "options.IntegrationLease");
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
    /// transaction; an instance is removed only once its history is archived. Only the runtime that cleans uses it: with
    /// <see cref="CleanupMode.Separate"/>, register it on the cleaner's runtime.
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
    [SuppressMessage("ApiDesign", "RS0026", Justification = "The overloads ship together in the package's first release and never bind ambiguously.")]
    public async Task<Decision> SendAsync(ICommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var envelope = Envelope(command);
        IntegrationCall? call = null;
        await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            // An integration calls the outside world once, before any transaction: a conflict records its outcome again.
            var engine = Engine(db, _time.GetUtcNow());
            if (engine.IsIntegrationCommand(envelope))
            {
                call = await engine.CallAsync(envelope, cancellationToken).ConfigureAwait(false);
            }
        }

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
                await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
                var decision = (await Engine(db, _time.GetUtcNow()).HandleAsync(envelope, call, cancellationToken).ConfigureAwait(false))!;
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
    /// <see cref="ConcurrencyConflictException"/> for you to retry, since the transaction is yours. An integration command
    /// sent this way calls the outside world inside your transaction.
    /// </summary>
    [SuppressMessage("ApiDesign", "RS0026", Justification = "The overloads ship together in the package's first release and never bind ambiguously.")]
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
            var decision = (await Engine(context, _time.GetUtcNow()).HandleAsync(Envelope(command), cancellationToken).ConfigureAwait(false))!;
            // Early, before your commit: a worker that finds nothing yet goes back to waiting, and sees the message on its
            // next look, at most MaxPollInterval later.
            Wake();
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

    /// <inheritdoc />
    public IReadOnlyList<string> Subscriptions => _application.Subscriptions;

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

    private Envelope Envelope(ICommand command)
    {
        var (type, payload) = _application.Types.Serialize(command, command.GetType());
        return new Envelope(Guid.CreateVersion7(), MessageKind.Command, type, payload);
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

                // One loop of this runtime catches up at a time, a bounded batch per pass, between messages. Seeding is
                // work: a worker that seeded doesn't back off before the next batch.
                var seeded = 0;
                if (_application.Registry.CatchingUp.Any() && Interlocked.CompareExchange(ref _catchingUp, 1, 0) == 0)
                {
                    try
                    {
                        seeded = await CatchUpPassAsync(CatchUpBatchPerPass, cancellationToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        Volatile.Write(ref _catchingUp, 0);
                    }
                }

                if (_options.Cleanup == CleanupMode.Workers
                    && ClaimTurn(ref _lastCleanup, _options.CleanupInterval)
                    && (await ForgetExpiredAsync(BatchSize, CleanupBatchesPerPass, cancellationToken).ConfigureAwait(false)).More)
                {
                    // A pass removes a bounded amount, so the worker gets back to messages: the next loop continues.
                    Interlocked.Exchange(ref _lastCleanup, 0);
                }

                if (ClaimTurn(ref _lastHealth, _options.HealthInterval))
                {
                    QueueMetrics.Record(await QueueNameAsync(cancellationToken).ConfigureAwait(false), await HealthAsync(cancellationToken).ConfigureAwait(false));
                }

                if (handled == 0 && relayed == 0 && seeded == 0)
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
    /// A cleaner: removes what the retention no longer keeps, one bounded pass every
    /// <see cref="EfCoreRuntimeOptions.CleanupInterval"/> (the next one at once while more expired), until cancelled. Run it
    /// in a service of its own, with <see cref="CleanupMode.Separate"/> on the runtimes that run workers. One cleaner is
    /// enough; two are safe (a row already removed is not removed again, and an archive accepts a history twice). The
    /// cleaner archives histories, so register <see cref="UseHistoryArchive"/> on its runtime: without one, it deletes them.
    /// A failed pass is logged and tried again after the interval.
    /// </summary>
    public async Task RunCleanupAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var wait = _options.CleanupInterval;
            try
            {
                var pass = await ForgetExpiredAsync(BatchSize, CleanupBatchesPerPass, cancellationToken).ConfigureAwait(false);
                LogCleanupPass(pass.Removed, pass.More);
                if (pass.More)
                {
                    wait = TimeSpan.Zero;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Nothing is half removed: each batch is its own statement or transaction.
                LogCleanupFailed(ex);
            }

            if (wait > TimeSpan.Zero)
            {
                try
                {
                    await Task.Delay(wait, _time, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
            }
        }
    }

    /// <summary>
    /// Waits <paramref name="delay"/>, or less if this runtime queues a message meanwhile. The signal stays set until a
    /// worker takes it, so a message queued while the worker was looking still ends the next wait at once.
    /// </summary>
    private async Task IdleAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _idleWaits);
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

    /// <summary>How many times a worker of this runtime started waiting idle (tests wait for it instead of for time).</summary>
    internal int IdleWaits => Volatile.Read(ref _idleWaits);

    /// <summary>
    /// Claims the turn of something workers do every <paramref name="interval"/>: true for one worker loop once it is due.
    /// </summary>
    private bool ClaimTurn(ref long last, TimeSpan interval)
    {
        var now = _time.GetUtcNow().UtcTicks;
        var seen = Interlocked.Read(ref last);
        return now - seen >= interval.Ticks && Interlocked.CompareExchange(ref last, now, seen) == seen;
    }

    /// <summary>
    /// Something was queued: an idle worker looks now instead of after its wait. A no-op once the runtime is disposed:
    /// what was committed is in the queue anyway, and the caller must not see a failure for it.
    /// </summary>
    private void Wake()
    {
        if (_disposed || _work.CurrentCount > 0)
        {
            return;
        }

        try
        {
            _work.Release();
        }
        catch (Exception ex) when (ex is SemaphoreFullException or ObjectDisposedException)
        {
            // Another caller set the signal first, or the runtime was disposed meanwhile.
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

        Envelope envelope;
        DateTimeOffset? leasedUntil = null;
        try
        {
            envelope = JsonSerializer.Deserialize(message.Envelope, WyrdJsonContext.Default.Envelope)!;
            var engine = Engine(db, now);
            if (engine.IsIntegrationCommand(envelope))
            {
                // Leased, not locked: the outside call is made with no transaction open (CallLeasedAsync).
                leasedUntil = Microseconds(now + _options.IntegrationLease);
                await db.Set<QueueRow>().Where(r => r.Id == message.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(r => r.AvailableAt, leasedUntil.Value), cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await engine.HandleAsync(envelope, cancellationToken).ConfigureAwait(false);
                await db.Set<QueueRow>().Where(r => r.Id == message.Id).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            if (leasedUntil is null)
            {
                QueueMetrics.Handled.Add(1, QueueTag(db));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            await RedeliverLaterAsync(message, ex, now).ConfigureAwait(false);
            return true;
        }

        if (leasedUntil is { } until)
        {
            await CallLeasedAsync(message, envelope, until, cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    /// <summary>
    /// An integration command, leased instead of locked: the outside call is made with no transaction open, so a slow
    /// third party holds no connection and no lock; then its outcome is recorded in a new transaction, if the lease is
    /// still this worker's. A worker that stops in between leaves the lease to expire, and another one calls again with
    /// the same idempotency key.
    /// </summary>
    private async Task CallLeasedAsync(QueueRow message, Envelope envelope, DateTimeOffset leasedUntil, CancellationToken cancellationToken)
    {
        IntegrationCall call;
        try
        {
            await using var reader = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            call = await Engine(reader, _time.GetUtcNow()).CallAsync(envelope, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            await RedeliverLaterAsync(message, ex, _time.GetUtcNow(), leasedUntil).ConfigureAwait(false);
            return;
        }

        for (var attempt = 1; ; attempt++)
        {
            var now = _time.GetUtcNow();
            await using var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            // By id only: an outcome that arrives after the lease ran out is still a fact, and recorded, or a call that
            // always outlives its lease would never be recorded. While the row is there nobody has recorded one yet;
            // writing it locks it until the commit, so another worker that called meanwhile finds it gone.
            var row = db.Set<QueueRow>().Where(r => r.Id == message.Id);
            try
            {
                if (await row.ExecuteUpdateAsync(s => s.SetProperty(r => r.Attempts, r => r.Attempts), cancellationToken).ConfigureAwait(false) == 0)
                {
                    LogLeaseLost(message.Id);
                    return;
                }

                await Engine(db, now).HandleAsync(envelope, call, cancellationToken).ConfigureAwait(false);
                await row.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                QueueMetrics.Handled.Add(1, QueueTag(db)); // only the worker that recorded it: a lost or released lease isn't handled
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                if (attempt < _options.MaxConflictRetries && IsConflict(ex))
                {
                    // The call's outcome is a fact: record it again, don't call again.
                    LogConflict(message.Id);
                    continue;
                }

                if (ex is IntegrationCallNeededException || IsConflict(ex))
                {
                    // Handled again right away, from the start.
                    await db.Set<QueueRow>().Where(r => r.Id == message.Id && r.AvailableAt == leasedUntil)
                        .ExecuteUpdateAsync(s => s.SetProperty(r => r.AvailableAt, now), CancellationToken.None).ConfigureAwait(false);
                    return;
                }

                await RedeliverLaterAsync(message, ex, now, leasedUntil).ConfigureAwait(false);
                return;
            }
        }
    }

    /// <summary>PostgreSQL keeps microseconds: a lease compared for equality must not carry more.</summary>
    private static DateTimeOffset Microseconds(DateTimeOffset at) => new(at.Ticks - at.Ticks % TimeSpan.TicksPerMicrosecond, at.Offset);

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
        await CatchUpPassAsync(batchSize, cancellationToken).ConfigureAwait(false);
        return !_application.Registry.CatchingUp.Any();
    }

    /// <summary>One pass of catching up; returns how many states it seeded.</summary>
    private async Task<int> CatchUpPassAsync(int batchSize, CancellationToken cancellationToken)
    {
        var seeded = 0;
        foreach (var projection in _application.Registry.CatchingUp.ToList())
        {
            var viewType = projection.View.FullName!;
            var batches = new List<IReadOnlyList<StoredState>>();
            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                var store = new EfCoreStore(db, _time.GetUtcNow());

                // After a restart, or on another instance of the service, only the store knows: caught up once, for good.
                if (await store.IsCaughtUpAsync(viewType, cancellationToken).ConfigureAwait(false))
                {
                    _application.Registry.CaughtUp(projection);
                    continue;
                }

                foreach (var seed in projection.Seeds)
                {
                    batches.Add(await store.UnseededAsync(viewType, seed.FullName!, batchSize, cancellationToken).ConfigureAwait(false));
                }
            }

            foreach (var state in batches.SelectMany(b => b))
            {
                if (await CatchUpStepAsync(projection, state, engine => engine.SeedAsync(projection, state, cancellationToken), cancellationToken)
                    .ConfigureAwait(false))
                {
                    seeded++;
                }
            }

            await CatchUpStepAsync(projection, state: null, async engine => await engine.CompleteCatchUpAsync(projection, cancellationToken).ConfigureAwait(false), cancellationToken)
                .ConfigureAwait(false);
        }

        return seeded;
    }

    private const int CatchUpBatchPerPass = 100;

    /// <summary>
    /// One step of a catch-up in its own transaction. A conflict (another worker seeded the stream, or an event changed
    /// the state meanwhile) leaves it for the next pass; any other failure is logged with what it was about, and the
    /// projection stays catching up, its other states seeded. Returns whether the step committed.
    /// </summary>
    private async Task<bool> CatchUpStepAsync(IProjectionHandler projection, StoredState? state, Func<Engine, ValueTask> step, CancellationToken cancellationToken)
    {
        try
        {
            await using var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await step(Engine(db, _time.GetUtcNow())).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (IsConflict(ex))
        {
            LogConflictRetry("a catch-up", 1);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogCatchUpFailed(projection.View.Name, state?.StateType ?? "-", state?.Key ?? "-", ex);
        }

        return false;
    }

    /// <summary>
    /// Removes what the retention no longer keeps (Q2.3), in batches of <paramref name="batchSize"/> so no transaction
    /// holds many locks; returns how many rows went. Suspended and running instances stay; a removed instance takes its
    /// parked events with it. Workers call it every <see cref="EfCoreRuntimeOptions.CleanupInterval"/>, unless cleanup is
    /// <see cref="CleanupMode.Separate"/>: then a scheduled job can call it, and it removes everything that expired.
    /// </summary>
    public async Task<int> ForgetExpiredAsync(int batchSize = BatchSize, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);
        return (await ForgetExpiredAsync(batchSize, int.MaxValue, cancellationToken).ConfigureAwait(false)).Removed;
    }

    private const int BatchSize = 1_000;

    /// <summary>How many batches of each kind a worker removes per pass before it gets back to messages.</summary>
    private const int CleanupBatchesPerPass = 10;

    /// <summary>What one cleanup pass removed, and whether it stopped at its limit with more expired left.</summary>
    private sealed class CleanupPass(int maxBatches)
    {
        public int MaxBatches { get; } = maxBatches;

        public int Removed { get; set; }

        public bool More { get; set; }
    }

    private async Task<CleanupPass> ForgetExpiredAsync(int batchSize, int maxBatches, CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();
        var pass = new CleanupPass(maxBatches);
        if (_retention.Deduplication.Cutoff(now) is { } dedup)
        {
            // A fence blocks its command forever (semantics 3.4): it never expires.
            await ForgetAsync<InboxRow, Guid>(pass, r => r.RecordedAt <= dedup && !r.Fence, r => r.MessageId, batchSize, cancellationToken).ConfigureAwait(false);
            await ForgetAsync<HandledRow, Guid>(pass, r => r.HandledAt <= dedup, r => r.MessageId, batchSize, cancellationToken).ConfigureAwait(false);
        }

        if (_retention.FinishedInstances.Cutoff(now) is { } finished)
        {
            await ForgetFinishedInstancesAsync(pass, finished, batchSize, cancellationToken).ConfigureAwait(false);
        }

        if (_retention.RelayedMessages.Cutoff(now) is { } relayed)
        {
            // Without a transport nothing leaves: a published message expires from when it was published.
            await (_transport is null
                ? ForgetAsync<PublishedRow, long>(pass, r => r.RecordedAt <= relayed, r => r.Seq, batchSize, cancellationToken)
                : ForgetAsync<PublishedRow, long>(pass, r => r.RelayedAt <= relayed, r => r.Seq, batchSize, cancellationToken)).ConfigureAwait(false);
        }

        if (_retention.Events.Cutoff(now) is { } events)
        {
            await ForgetAsync<EventRow, long>(pass, r => r.RecordedAt <= events, r => r.Seq, batchSize, cancellationToken).ConfigureAwait(false);
        }

        if (_retention.DeadLetters.Cutoff(now) is { } dead)
        {
            await ForgetAsync<DeadLetterRow, long>(pass, r => r.FailedAt <= dead, r => r.Id, batchSize, cancellationToken).ConfigureAwait(false);
        }

        if (pass.Removed > 0)
        {
            QueueMetrics.Removed.Add(pass.Removed, new KeyValuePair<string, object?>(QueueMetrics.QueueTagName, await QueueNameAsync(cancellationToken).ConfigureAwait(false)));
        }

        return pass;
    }

    private async Task ForgetAsync<TRow, TKey>(
        CleanupPass pass, Expression<Func<TRow, bool>> expired, Expression<Func<TRow, TKey>> key, int batchSize, CancellationToken cancellationToken)
        where TRow : class
    {
        for (var batches = 0; ; batches++)
        {
            if (batches == pass.MaxBatches)
            {
                pass.More = true;
                return;
            }

            await using var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var batch = await db.Set<TRow>().Where(expired).OrderBy(key).Take(batchSize).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            pass.Removed += batch;
            if (batch < batchSize)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Finished instances whose retention passed, in batches. With an archive, each batch is read whole (its histories)
    /// outside any transaction, archived, and only then removed; a failed archive keeps the batch for the next pass.
    /// </summary>
    private async Task ForgetFinishedInstancesAsync(CleanupPass pass, DateTimeOffset cutoff, int batchSize, CancellationToken cancellationToken)
    {
        // Whole rows are read for the archive: smaller batches keep the worker's memory flat.
        var size = _archive is null ? batchSize : Math.Min(batchSize, ArchiveBatchSize);
        for (var batches = 0; ; batches++)
        {
            if (batches == pass.MaxBatches)
            {
                pass.More = true;
                return;
            }

            // Each batch adds what it removed to the pass, and says how full it was.
            var batch = _archive is null
                ? await ForgetFinishedBatchAsync(pass, cutoff, size, cancellationToken).ConfigureAwait(false)
                : await ArchiveFinishedBatchAsync(pass, _archive, cutoff, size, cancellationToken).ConfigureAwait(false);
            if (batch is not { } full)
            {
                return; // the archive failed: the batch stays for the next pass
            }

            if (full < size)
            {
                return;
            }
        }
    }

    private const int ArchiveBatchSize = 100;

    private async Task<int?> ForgetFinishedBatchAsync(CleanupPass pass, DateTimeOffset cutoff, int size, CancellationToken cancellationToken)
    {
        await using var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // Set-based, each delete checking the condition again: a parked event goes only while its instance is still a
        // finished, expired row, so the event of a new instance that re-used the key (its row isn't finished, and can only
        // exist once the old one is gone) is never taken with it. Parked events go first, in batches of their own (an
        // instance can have several); what is left of them goes with the instances, in one transaction.
        var parked = db.Set<ParkedRow>()
            .Where(p => db.Set<InstanceRow>().Any(i => i.Process == p.Process && i.Key == p.Key && i.FinishedAt <= cutoff));
        var parkedBatch = await parked.OrderBy(p => p.Process).ThenBy(p => p.Key).ThenBy(p => p.Step).Take(size)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        pass.Removed += parkedBatch;
        if (parkedBatch == size)
        {
            return size; // more parked events first: the next batch goes on
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        pass.Removed += await parked.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        var removed = await db.Set<InstanceRow>()
            .Where(r => r.FinishedAt <= cutoff).OrderBy(r => r.FinishedAt).Take(size)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        pass.Removed += removed;
        return removed;
    }

    private async Task<int?> ArchiveFinishedBatchAsync(
        CleanupPass pass, IHistoryArchive archive, DateTimeOffset cutoff, int size, CancellationToken cancellationToken)
    {
        List<InstanceRow> finished;
        await using (var reader = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            finished = await reader.Set<InstanceRow>().AsNoTracking()
                .Where(r => r.FinishedAt <= cutoff).OrderBy(r => r.FinishedAt).Take(size)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
        }

        if (finished.Count == 0)
        {
            return 0;
        }

        // Archived before anything is removed, with no transaction open.
        try
        {
            // WaitAsync: a worker that is stopping doesn't wait for an archive that ignores the token; nothing was removed yet.
            await archive.ArchiveAsync([.. finished.Select(HistoryOf)], cancellationToken).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogArchiveFailed(finished.Count, ex);
            return null;
        }

        await using var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var removed = 0;
        foreach (var instance in finished)
        {
            // Parked events first, through the instance that must still be a finished, expired row: another cleanup that
            // removed it meanwhile may have freed the key for a new instance, whose early events stay.
            pass.Removed += await db.Set<ParkedRow>()
                .Where(p => p.Process == instance.Process && p.Key == instance.Key
                    && db.Set<InstanceRow>().Any(i => i.Process == instance.Process && i.Key == instance.Key && i.FinishedAt <= cutoff))
                .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            removed += await db.Set<InstanceRow>()
                .Where(r => r.Process == instance.Process && r.Key == instance.Key && r.FinishedAt <= cutoff)
                .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        pass.Removed += removed;

        // Another cleanup may have removed some meanwhile: the batch counts as full when its read was.
        return finished.Count;
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

    /// <summary>
    /// Records a failed delivery. For a leased message (<paramref name="leasedUntil"/>), only while the lease is still
    /// this worker's: once it ran out, another worker may hold the row, and its failure or outcome is that worker's to
    /// record.
    /// </summary>
    private async Task RedeliverLaterAsync(QueueRow message, Exception error, DateTimeOffset now, DateTimeOffset? leasedUntil = null)
    {
        // A concurrency conflict is normal between workers: try again right away, without counting it as a failure.
        if (IsConflict(error))
        {
            LogConflict(message.Id);
            return;
        }

        // The rollback released the row lock, so another worker may have claimed the message meanwhile. Every branch
        // first takes the row back, unchanged since this attempt, and writes the failure only when that succeeded; if
        // the other worker still holds it, the statement waits for its commit and then finds nothing.
        await using var db = await _contexts.CreateDbContextAsync(CancellationToken.None).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(CancellationToken.None).ConfigureAwait(false);
        QueueMetrics.Failed.Add(1, QueueTag(db));
        var unchanged = db.Set<QueueRow>().Where(r => r.Id == message.Id && r.Attempts == message.Attempts);
        if (leasedUntil is { } lease)
        {
            unchanged = unchanged.Where(r => r.AvailableAt == lease);
        }

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

            db.Add(new DeadLetterRow { Id = message.Id, Envelope = message.Envelope, Attempts = failed, Error = error.ToString(), FailedAt = now });
            await db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
            LogDeadLetter(message.Id, failed, error); // once the row exists
            QueueMetrics.DeadLettered.Add(1, QueueTag(db));
            return;
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

    /// <summary>
    /// Releases the workers' wake-up signal and stops publishing this queue's health. Stop the workers
    /// (<see cref="RunAsync"/>) first.
    /// </summary>
    public void Dispose()
    {
        _disposed = true;
        if (_queueName is { } queue)
        {
            QueueMetrics.Forget(queue);
        }

        _work.Dispose();
    }

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
            .FirstOrDefaultAsync(r => r.ViewType == viewType && r.Key == name && r.Type != ViewRow.Deleted, cancellationToken).ConfigureAwait(false);
        return row is null ? null : (TView)_application.Types.Deserialize(row.Type, row.Payload);
    }

    /// <summary>Every document of a read model, in creation order.</summary>
    public async Task<IReadOnlyList<TView>> ViewsAsync<TView>(CancellationToken cancellationToken = default)
        where TView : class
    {
        var viewType = typeof(TView).FullName!;
        await using var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var rows = await db.Set<ViewRow>().AsNoTracking()
            .Where(r => r.ViewType == viewType && r.Type != ViewRow.Deleted).OrderBy(r => r.Created).ThenBy(r => r.Key)
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
            Suspended: await db.Set<InstanceRow>().LongCountAsync(r => r.Status == "Suspended", cancellationToken).ConfigureAwait(false),
            // Answered by the finished_at index; nothing expires while the retention keeps finished instances forever.
            ExpiredKept: _retention.FinishedInstances.Cutoff(now) is { } cutoff
                ? await db.Set<InstanceRow>().LongCountAsync(r => r.FinishedAt <= cutoff, cancellationToken).ConfigureAwait(false)
                : 0);

        static TimeSpan Age(DateTimeOffset? since, DateTimeOffset now) => since < now ? now - since.Value : TimeSpan.Zero;
    }

    /// <summary>
    /// The queue's name in the metrics: its database, schema and table (never the connection string), so runtimes of one
    /// process on different databases don't overwrite each other's health.
    /// </summary>
    private async ValueTask<string> QueueNameAsync(CancellationToken cancellationToken)
    {
        if (_queueName is { } known)
        {
            return known;
        }

        await using var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return QueueName(db);
    }

    private string QueueName(DbContext db)
    {
        if (_queueName is { } known)
        {
            return known;
        }

        var queue = db.Model.FindEntityType(typeof(QueueRow))!;
        return _queueName = $"{db.Database.GetDbConnection().Database}/{queue.GetSchema()}.{queue.GetTableName()}";
    }

    /// <summary>The counters carry the queue too, like the health gauges: runtimes of one process share the meter.</summary>
    private KeyValuePair<string, object?> QueueTag(DbContext db) => new(QueueMetrics.QueueTagName, QueueName(db));

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

    [LoggerMessage(Level = LogLevel.Debug, Message = "Cleanup removed {Removed} rows (more expired: {More})")]
    private partial void LogCleanupPass(int removed, bool more);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cleanup failed (database unavailable?); expired rows stay until the next pass")]
    private partial void LogCleanupFailed(Exception error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "History archive failed: {Count} finished instances kept for the next cleanup")]
    private partial void LogArchiveFailed(int count, Exception error);

    [LoggerMessage(Level = LogLevel.Error, Message = "Catching up {View} failed on {StateType} '{Key}': the view stays catching up, the rest goes on")]
    private partial void LogCatchUpFailed(string view, string stateType, string key, Exception error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Integration command {Id}: another worker recorded its outcome first, this one is not recorded twice")]
    private partial void LogLeaseLost(long id);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Message {Id} was taken by another worker meanwhile: its failure is not recorded twice")]
    private partial void LogTakenMeanwhile(long id);
}
