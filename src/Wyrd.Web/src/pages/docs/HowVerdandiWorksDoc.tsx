import source from '../../../../../docs/guides/how-verdandi-works.md?raw'
import { CodeBlock } from '../../components/common/CodeBlock'
import { DocArticle } from './DocArticle'
import { guideLink } from './guideLinks'
import { BenchmarkCharts } from './verdandi/BenchmarkCharts'
import { CleanupTopology } from './verdandi/CleanupTopology'
import { DataMap } from './verdandi/DataMap'
import { FailureScenarios } from './verdandi/FailureScenarios'
import { MessageLifecycle } from './verdandi/MessageLifecycle'
import { SkipLocked } from './verdandi/SkipLocked'

/**
 * docs/guides/how-verdandi-works.md stays the source of truth (GitHub, NuGet). This page tells the same
 * story with pictures; its two text diagrams are read from the guide itself, so they can't drift.
 */
const [PIECES, ORDER] = [...source.matchAll(/```text\n([\s\S]*?)```/g)].map(
    (m) => m[1],
)

export function HowVerdandiWorksDoc() {
    return (
        <DocArticle
            eyebrow="Guides · architecture"
            title="How Verdandi works"
            lead="Verdandi is Wyrd’s durable host: EfCoreRuntime on PostgreSQL or SQL Server. What happens underneath, so you can run it in production without reading its code."
        >
            <p>
                Where every piece of data lives, what one message does to the
                database, how many workers and API instances share the work,
                what fails and how, and where the limits are. How to set it up
                is in{' '}
                <a href={guideLink('running-durable.md')}>Running durable</a>;
                the exact rules are in{' '}
                <a href={guideLink('../design/semantics.md')}>
                    How Wyrd behaves
                </a>
                .
            </p>

            <h2>The pieces</h2>
            <CodeBlock code={PIECES} language="text" title="one service" />
            <ul>
                <li>
                    <strong>The engine</strong> decides; it never touches
                    storage or the network. Given one message it calls your pure
                    code and returns everything that message changed.
                </li>
                <li>
                    <strong>The runtime</strong> is one object per process (a
                    singleton) with no state of its own between calls: every
                    call opens its own <code>DbContext</code> and its own
                    transaction.
                </li>
                <li>
                    <strong>The database is the queue and the outbox.</strong>{' '}
                    There is no broker inside a service: everything the engine
                    produces is written as rows, in the same transaction as the
                    state it changed.
                </li>
                <li>
                    <strong>Workers</strong> are loops (<code>RunAsync</code>)
                    that take the next due message from the <code>queue</code>{' '}
                    table. Run as many as you like, in one process or in many.
                    Between messages they also remove what expired.
                </li>
            </ul>

            <h2>Where the data lives</h2>
            <DataMap />
            <p>
                Business data (<code>states</code>, <code>views</code>) grows
                with your business, like any table of yours. Everything else is
                delivery machinery: needed for a while, and then the retention
                you chose removes it, except suspended instances and fences,
                which never expire.
            </p>

            <h2>One message, one transaction</h2>
            <p>
                Every message is handled in one transaction that also takes it
                off the queue. Either all of it happens or none of it does. Step
                through one, then crash it before the commit.
            </p>
            <MessageLifecycle />
            <p>
                Integrations are the exception: a queued call to another system
                is made with no transaction open (the lease, under{' '}
                <em>When things fail</em>), and an integration command sent with{' '}
                <code>SendAsync(command, context)</code> calls the other system
                inside your transaction.
            </p>

            <h2>One order, end to end</h2>
            <p>
                <code>SendAsync</code> only handles the command itself and
                returns its decision. What the command causes is queued in the
                same transaction and handled by workers right after, so the
                request never waits for the rest of the process:
            </p>
            <CodeBlock code={ORDER} language="text" title="the Shop’s order" />

            <h2>Many workers, many instances</h2>
            <p>
                <code>SKIP LOCKED</code> makes every worker pass over rows
                another worker is handling, instead of waiting for them. That is
                how any number of workers share one queue table without a
                broker: in one process (call <code>RunAsync</code> several
                times) or in every instance of your service.
            </p>
            <SkipLocked />
            <p>
                A worker that finds nothing waits 250 ms, doubling up to 2 s
                while the queue stays empty, and is back to full speed as soon
                as there is work. A message queued by its own process wakes it
                at once; one queued by another process is seen on its next look.
            </p>

            <h2>Duplicates</h2>
            <p>
                Messages are delivered at least once: a worker can crash after
                doing the work and before committing, a broker can deliver
                twice. Effects happen once, for as long as the{' '}
                <code>Deduplication</code> retention remembers a message id: the{' '}
                <code>inbox</code> keeps each command’s outcome,{' '}
                <code>handled</code> each event’s id.
            </p>
            <p>
                A caller retrying is not a duplicate: every{' '}
                <code>SendAsync</code> is a new message with a new id. If your
                client may send the same command twice (a lost HTTP response, a
                retry), make the command idempotent in your domain: a{' '}
                <code>PlaceOrder</code> for an order that exists is rejected,
                not placed again.
            </p>

            <h2>When things fail</h2>
            <FailureScenarios />
            <p>
                <strong>The database is down?</strong> Workers log it and try
                again; queued messages are safe in the table.{' '}
                <strong>Timers</strong> are queue rows too: a wait’s timeout is
                a row whose availability time is the deadline, deleted in the
                same transaction as the awaited event. Nothing lives in memory,
                so a restart loses no timer.
            </p>

            <h2>Concurrency: what scales and what doesn’t</h2>
            <p>
                The runtime being a singleton is not a bottleneck: it shares
                nothing between calls. Two things decide how much load a service
                takes: <strong>connections</strong> (every{' '}
                <code>SendAsync</code> and every worker transaction holds one
                from the pool while it runs; Npgsql’s pool has 100 by default)
                and <strong>hot keys</strong> (requests for the same key are
                serialized by the database: one wins at a time, the others
                retry).
            </p>
            <BenchmarkCharts />
            <ul>
                <li>
                    Different keys scale with callers until the machine is
                    saturated.
                </li>
                <li>
                    One key takes one write at a time, whatever the number of
                    callers. When many hit it at once, some run out of attempts
                    and get a <code>ConcurrencyConflictException</code>: retry
                    later, or model the hot spot differently.
                </li>
                <li>
                    Workers never fail on a conflict: they roll back and handle
                    the message again, so a hot key only costs them time.
                </li>
                <li>
                    <strong>
                        Latency to the database is what a message costs.
                    </strong>{' '}
                    A message makes about 6 round trips, so every millisecond
                    between the workers and the database costs about 6 ms per
                    message. Keep the database close to the workers, and
                    register the context factory pooled (
                    <code>AddPooledDbContextFactory</code>).
                </li>
            </ul>

            <h2>Retention and cleanup</h2>
            <p>
                Delivery machinery is removed when the retention you chose says
                so. The host doesn’t start without a retention, and some things
                never expire: a suspended instance waits for an operator, and a
                fence (a command given up on without knowing whether it ran)
                stays blocked forever. A pass removes a bounded amount, in
                batches of 1,000 rows (100 finished instances when there is a
                history archive), each batch short so it holds few locks for a
                short time.
            </p>
            <CleanupTopology />
            <p>
                With a history archive (<code>IHistoryArchive</code>), a
                finished instance’s history is handed to it before the row is
                removed, and the row is removed only once the archive returned:
                a failed archive keeps it for the next pass. Delivery is at
                least once, so an archive must accept the same history twice. A
                failed batch is tried again, oldest first, so a history the
                archive can never store blocks the removal of the instances
                behind it: the archive must store it elsewhere rather than fail
                for it.
            </p>

            <h2>What to watch</h2>
            <p>
                Workers publish the queue’s health on the <code>Wyrd</code>{' '}
                meter: the queue’s lag, steps waiting for a slow third party,
                instances that need an operator, dead letters, finished
                instances kept past their retention, and counters of handled,
                failed and dead-lettered messages. The instruments and what to
                alert on are in{' '}
                <a href={guideLink('running-durable.md#health')}>
                    Running durable
                </a>
                .
            </p>
        </DocArticle>
    )
}
