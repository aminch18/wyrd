# Running durable

The same module that runs in memory in your tests runs durable in production. Nothing in your slices, processes or
projections changes: you swap the host. This guide takes the Shop from `InMemoryRuntime` to the EF Core host
(`Wyrd.EntityFrameworkCore`, PostgreSQL or SQL Server), then connects services through RabbitMQ (`Wyrd.RabbitMq`).
The behaviour behind every step is in [How Wyrd behaves](../design/semantics.md), and what happens underneath in
[How Verdandi works](how-verdandi-works.md).

## One module, any host

Register the module against `IWyrdBuilder<T>`, and write application code against `IWyrdRuntime`. Both hosts implement
both:

```csharp
public static TRuntime Register<TRuntime>(TRuntime runtime)
    where TRuntime : IWyrdBuilder<TRuntime> =>
    runtime
        .AddSlice(new PlaceOrderSlice())
        .AddIntegration(new PaymentGateway(http))
        .AddProjection(new OrderSummaries())
        .AddProcess(Processes.OrderFulfillment);
```

An endpoint depends on `IWyrdRuntime`, never on a host:

```csharp
app.MapPost("/orders", async (PlaceOrderRequest request, IWyrdRuntime wyrd) =>
    await wyrd.SendAsync(request.ToCommand()) switch
    {
        Decision.Accepted => Results.Accepted(),
        Decision.Rejected rejected => Results.Problem(rejected.Error.ToString(), statusCode: 409),
    });
```

## Wyrd's tables are part of your model

Wyrd's tables live in your own `DbContext`, so they are part of your migrations, and a table of yours commits in the
same transaction as Wyrd's:

```csharp
public sealed class ShopDbContext(DbContextOptions<ShopDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.AddWyrd();
}
```

Migrations belong to the application: `dotnet ef migrations add InitialWyrd`, applied as a deployment step.

## Create the host: retention is your decision

The EF Core host doesn't start without a `Retention`, and every member of it is required. How long duplicates are
recognised, how long finished instances and their histories stay, what happens to relayed messages, events and dead
letters: each line is a decision, written where the host is created. This is the Shop's
(`samples/Shop.Api/Durable/DurableHost.cs`):

```csharp
private static readonly Retention Retention = new()
{
    Deduplication = Keep.For(7.Days),          // far longer than any redelivery
    FinishedInstances = Keep.For(30.Days),     // a month to look into an order's process, or replay it with Urd
    RelayedMessages = Keep.For(7.Days),        // only the outbox's copy: the facts stay in the events
    Events = Keep.Forever("the shop's record of what happened"),
    DeadLetters = Keep.Forever("an operator reviews each one"),
};

services.AddPooledDbContextFactory<ShopDbContext>(options => options.UseNpgsql(connectionString));
services.AddSingleton(provider => ShopModule.Register(new EfCoreRuntime<ShopDbContext>(
    provider.GetRequiredService<IDbContextFactory<ShopDbContext>>(),
    ShopJson.Default,
    Retention,
    logger: provider.GetRequiredService<ILogger<EfCoreRuntime<ShopDbContext>>>())));
services.AddSingleton<IWyrdRuntime>(provider => provider.GetRequiredService<EfCoreRuntime<ShopDbContext>>());
services.AddHostedService<WyrdWorker>();
```

Register the factory pooled (`AddPooledDbContextFactory`): Wyrd opens a context for every message, and a pooled one
costs less. Measured on the Shop, a message took about 8% less time than with `AddDbContextFactory`
(`benchmarks/README.md`).

Suspended instances never expire: they wait for an operator. Fences never expire either: a command given up on
without knowing whether it ran stays blocked forever. To keep finished histories after they leave the hot tables,
give the host an `IHistoryArchive`: each batch is archived first, with no transaction open, and removed only once
the archive returned.

## Workers

The database is the queue. A worker takes the next due message and handles it in one transaction that also takes it
off the queue: state, events, instances, inbox, outbox, timers and read models commit together, or not at all. Run as
many workers as you like, in one process or many:

```csharp
public sealed class WyrdWorker(EfCoreRuntime<ShopDbContext> wyrd) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => wyrd.RunAsync(stoppingToken);
}
```

Workers also remove what expired, a bounded amount per pass, and measure the queue's health.

## Your transaction, or Wyrd's

`SendAsync(command)` runs in a transaction of its own. To commit your own changes together with Wyrd's, begin the
transaction and send into it; you commit it, and a concurrency conflict comes back as a
`ConcurrencyConflictException` for you to retry:

```csharp
await using var transaction = await db.Database.BeginTransactionAsync();
db.Add(new AuditEntry("order placed"));
var decision = await wyrd.SendAsync(new PlaceOrder("A-1", "KEYBOARD", 1, 89m), db);
await db.SaveChangesAsync();
await transaction.CommitAsync();
```

## Integrations: the only I/O

An integration owns a command carried out by another system. On the EF Core host its call is made with no
transaction open: the command is leased, the call made, and its outcome recorded in a second transaction. Pass the
idempotency key to the other system, so a retry or a second call never repeats the effect:

```csharp
public sealed class PaymentGateway(HttpClient http) : Integration<ChargePayment, PaymentCharged, PaymentError>
{
    public override async Task<Result<PaymentCharged, PaymentError>> ExecuteAsync(
        ChargePayment command, IntegrationContext context, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "charges");
        request.Headers.Add("Idempotency-Key", context.IdempotencyKey.ToString());
        // ... send it, then map the provider's answer:
        // accepted -> the event, declined -> the error. An exception (a timeout too) is a transient failure.
    }
}
```

Three things follow from talking to the outside world:

- A timeout of yours (`HttpClient`'s, for example) is a failure the step's retry policy handles, like any exception.
- A call slower than `EfCoreRuntimeOptions.IntegrationLease` (five minutes) may be made again by another worker,
  with the same key. Set the lease longer than your slowest call.
- A call that never answered may have acted. On compensation, its undo runs anyway, so an integration's undo treats
  "nothing to undo" (a refund of a charge that never happened) as done.

## Operators

What Wyrd can't resolve is suspended, never lost: a step whose retries ran out, an undo that was refused, a definition
that changed under a running instance. An operator decides:

```csharp
await wyrd.ResumeAsync(Processes.OrderFulfillment, "A-1");      // try again what it was doing, same message id
await wyrd.CompensateAsync(Processes.OrderFulfillment, "A-1");  // undo what it did
```

## Health

Slowness is observed, never turned into failures. `HealthAsync()` measures the queue, and workers publish it on the
`Wyrd` meter (`AddMeter("Wyrd")` in OpenTelemetry):

| Instrument | What to alert on |
| --- | --- |
| `wyrd.queue.due`, `wyrd.queue.oldest_due.age` | The queue's lag |
| `wyrd.process.pending_steps`, `wyrd.process.oldest_pending_step.age` | A third party that is slow to answer |
| `wyrd.process.suspended` | Instances that need an operator |
| `wyrd.dead_letters` | Messages that gave up |
| `wyrd.process.expired_kept` | Cleanup is behind, or the history archive keeps failing |
| `wyrd.messages.handled`, `wyrd.messages.failed`, `wyrd.messages.dead_lettered` | Throughput and failures |

## A read model added later

Wyrd keeps current state, not every past event. A projection added to a store that already has data catches up from
the states it names, then follows their events, with no event applied twice or missed:

```csharp
public sealed class StockLevels : Projection<StockLevel>
{
    public StockLevels()
    {
        Seed<StockItem>((sku, _) => sku, (_, sku, item) => new StockLevel(sku, item.Available));
        On<StockAdded>(e => e.Sku, (view, e) => new StockLevel(e.Sku, (view?.Available ?? 0) + e.Quantity));
    }
}
```

Workers seed it on their own; `CatchUpAsync()` does a pass on demand. Only what a state says can be rebuilt: a view
that counts past events starts from what the states show.

## Between services: RabbitMQ

Only integration events cross service boundaries. The outbox stays in the database; `Wyrd.RabbitMq` carries what
leaves it, with the contract as the routing key and the message id kept end to end:

```csharp
var connection = await new ConnectionFactory { Uri = new Uri(rabbitUrl) }.CreateConnectionAsync();

// What this service publishes leaves through the outbox relay, confirmed by the broker.
var payments = PaymentsModule.Register(new EfCoreRuntime<PaymentsDbContext>(contexts, PaymentsJson.Default, retention))
    .UseTransport(await RabbitMqTransport.CreateAsync(connection));

// What it uses arrives on its own queue, bound to the contracts it uses.
await using var subscriber = await RabbitMqSubscriber.StartAsync(connection, payments, queue: "payments");
```

Register every process, projection and upcaster before starting the subscriber: its queue is bound to the contracts
the runtime uses at that moment. A message is acknowledged once the runtime has it, so a crash before that delivers it
again, and the runtime handles the redelivery once.
