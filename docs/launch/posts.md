# Launch post drafts

Drafts for the owner to edit and post. Links: repo https://github.com/aminch18/wyrd, site https://aminch18.github.io/wyrd/,
discussions https://github.com/aminch18/wyrd/discussions.

Facts to keep straight in any edit: this is a preview (0.3.0); the in-memory runtime, analyzers, `Wyrd.Testing`,
`dotnet wyrd` and Skuld (with shrinking) exist today; replay of recorded instances (Urd) is next; the durable runtime (PostgreSQL, transports) is in progress; it is not for
production yet. The "85 of 200" figure is from Skuld's first run against the Shop sample, before the fixes.

## 1. LinkedIn (English)

I've been building an open-source .NET library called Wyrd, and the first preview is out.

The idea: DDD in the functional style for everyday C#. Your business rules are pure functions (command and state in, event or rejection out). A saga is a few declarative lines: steps, how each one retries, how it's undone, business deadlines. The framework owns the messy part: retries, compensation, idempotency. And when it can't resolve something, it stops the process and tells you why, instead of guessing. Integration events are versioned contracts, and breaking one is a compile error, not an incident.

The part I'm most curious about is Skuld, a deterministic simulator. It runs a scenario in hundreds of hostile futures (transient faults, duplicated messages, reordered events) and checks your invariants at the end of each. The first time I pointed it at our own sample shop, 85 of 200 futures broke. A compensation that couldn't retry lost stock after one network blip. Nothing in the happy-path tests had noticed.

It's a preview, not production-ready: the in-memory runtime, analyzers and simulation work today; the durable runtime (PostgreSQL, transports) is still in progress.

If you build event-driven systems in .NET, I'd value your feedback, especially on what feels wrong.

https://github.com/aminch18/wyrd

#dotnet #csharp #ddd #eventdriven

## 2. LinkedIn (Español)

Llevo un tiempo construyendo una librería open source para .NET llamada Wyrd, y ya hay una primera preview.

La idea: DDD en estilo funcional para C# de todos los días. Las reglas de negocio son funciones puras (comando y estado entran, evento o rechazo salen). Una saga son pocas líneas declarativas: pasos, cómo reintenta cada uno, cómo se deshace, plazos de negocio. El framework se ocupa de lo incómodo: reintentos, compensaciones, idempotencia. Y cuando no puede resolver algo, para el proceso y te dice por qué, en vez de adivinar. Los eventos de integración son contratos versionados, y romper uno es un error de compilación, no un incidente.

Lo que más curiosidad me da es Skuld, un simulador determinista. Ejecuta un escenario en cientos de futuros hostiles (fallos transitorios, mensajes duplicados, eventos desordenados) y comprueba tus invariantes al final de cada uno. La primera vez que lo lancé contra nuestra tienda de ejemplo, 85 de 200 futuros se rompieron. Una compensación que no podía reintentar perdía stock tras un solo corte de red. Los tests del camino feliz no habían notado nada.

Es una preview, todavía no apta para producción: el runtime en memoria, los analizadores y la simulación funcionan hoy; el runtime duradero (PostgreSQL, transportes) está en curso.

Si construyes sistemas orientados a eventos en .NET, me interesa tu opinión, sobre todo lo que te parezca raro o incorrecto.

https://github.com/aminch18/wyrd

#dotnet #csharp #ddd #eventdriven

## 3. r/dotnet

**Title:** Wyrd (preview): pure decisions, declarative sagas, versioned contracts checked at compile time, and a deterministic failure simulator

Hi r/dotnet. I've been working on Wyrd, an MIT-licensed .NET 10 / C# 14 library for event-driven processes, and I'd like feedback on the design before it hardens.

**The model.** A slice owns one command and exposes a pure `Decide(command, state) -> Result<Event, Error>`. State is a closed `[Union]` of records with an `Evolve(event)` method. A process is data:

```csharp
Process
    .StartOn<OrderPlaced>(e => e.OrderId)
    .Then(Stock.Reserve).Retry(5.Times).Undo(Stock.Release)
    .Then(Payments.Charge).Retry(5.Times, Backoff.Exponential).Undo(Payments.Refund)
    .Await<PaymentConfirmed>(e => e.OrderId).Within(15.Minutes)
    .Then(Shipping.Ship).Retry(5.Times)
    .OnCompensated(Orders.Cancel).Retry(5.Times);
```

Every step has to say how it retries (`.Retry(...)` or `.NoRetry("why")`, an analyzer asks if you forget); there is no default. Undos always insist: a compensation that gives up leaves the world inconsistent. When Wyrd can't resolve something (retries exhausted, a redeploy that changed the process shape) it suspends the instance with a reason, and an operator resumes or compensates it. There are no technical timeouts on steps: a slow third party only makes processes wait; time limits are business decisions (`.Within(15.Minutes)`).

User code never publishes, persists or schedules anything; the engine handles one envelope per transaction, with inbox/outbox semantics, deterministic message ids and a virtual clock.

**Contracts.** Integration events carry `[IntegrationEvent("payments.payment-charged", Version = 1)]` and are snapshotted in a reviewed `contracts.wyrd.txt`. Roslyn analyzers compare code and snapshot on every build: change `Amount` from `decimal` to `double` and you get `WYRD0201` as an error. Commands vs events, one owner per command and JSON-context completeness (for Native AOT) are analyzers too.

**Skuld.** `Skuld.SimulateAsync` runs a scenario in many seeded futures with transient faults, redelivery, reordering and timers, checks invariants, and reports each broken future shrunk to the fewest perturbations that still break it, with its journal and a seed you can replay. The first run against our own sample shop broke 85 of 200 futures. It found a compensation that couldn't retry (stock lost after one blip), a redelivered failure reply that consumed an extra retry, a command we'd given up on that ran later, and a read model that moved an order from Cancelled back to Released. All fixed; the shop now holds in 5,000 futures at 20% faults.

**Status.** Preview (0.3.0), not for production. Working: model, analyzers, in-memory runtime, Given/When/Then testing, `dotnet wyrd` (AsyncAPI and Mermaid), Skuld. Next: replay of recorded instances as tests (Urd), then the durable runtime (PostgreSQL, transports), which is already built and being hardened.

**What I'd like to hear:** does the pure-decision style read well to you? Where would you not want a framework owning retries and compensation? Is the contract snapshot workflow acceptable in review?

Repo: https://github.com/aminch18/wyrd · Docs: https://aminch18.github.io/wyrd/ · `git clone`, then `dotnet run --project samples/Shop.Console` to see it run.

## 4. X thread

**1/** I've released a first preview of Wyrd, an open-source .NET library for event-driven processes: pure decisions, declarative sagas, versioned contracts, deterministic failure simulation. Preview, feedback wanted. https://github.com/aminch18/wyrd

**2/** Business rules are pure functions: command + state in, event or rejection out. A saga is a few lines (steps, retry, undo, business deadlines). The framework owns retries, compensation and idempotency, so your code never publishes or persists. When it can't resolve something, it suspends the process with a reason instead of guessing.

**3/** Integration events are versioned contracts, snapshotted in a file you review in PRs. Change a field type and the build fails: `WYRD0201: Breaking change in integration event 'payments.payment-charged' v1`. Not an incident, a compile error.

**4/** Skuld is a deterministic simulator: faults, duplicates, reordering, timers, one seed per future, your invariants checked at the end. First run on our own sample shop: 85 of 200 futures broke. A compensation that couldn't retry lost stock after one network blip. Happy-path tests had passed.

**5/** Honest status: the in-memory runtime, analyzers, testing and Skuld work today. The durable runtime (PostgreSQL, transports) is in progress. Not for production yet. Try it in two minutes and tell me what feels wrong: https://aminch18.github.io/wyrd/
