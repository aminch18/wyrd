import { Callout } from '../../components/common/Callout'
import { CodeBlock } from '../../components/common/CodeBlock'
import { TerminalInstall } from '../../components/common/TerminalInstall'
import { SNIPPETS } from '../../data/docs'
import { DocArticle } from './DocArticle'

export function QuickStartDoc() {
    return (
        <DocArticle
            eyebrow="Start here"
            title="Quick start"
            lead="Build and run your first deterministic process with the in-memory runtime."
        >
            <h2>Install the package</h2>
            <p>
                Wyrd targets .NET 10 and C# 14. The package includes the
                analyzers; add <code>Wyrd.Testing</code> to your test project.
            </p>
            <TerminalInstall />
            <h2>Describe the messages</h2>
            <p>
                Commands are intentions. Domain events are internal facts.
                Integration events are explicit, versioned public contracts.
            </p>
            <CodeBlock code={SNIPPETS.messages} title="Payments.cs" />
            <h2>Keep the decision pure</h2>
            <p>
                A slice owns one command and decides against the current
                immutable state. Expected failure is a value, not an exception.
            </p>
            <CodeBlock code={SNIPPETS.slice} title="ChargePaymentSlice.cs" />
            <h2>Compose the process</h2>
            <p>
                Connect business steps, retries, compensation, and timeouts
                without exposing infrastructure to domain code.
            </p>
            <CodeBlock
                code={SNIPPETS.fluentProcess}
                title="OrderFulfillment.cs"
            />
            <Callout title="Run the complete sample">
                Clone the repository and run{' '}
                <code>dotnet run --project samples/Shop.Console</code>. The
                walkthrough includes happy paths, gateway failures, timeouts,
                compensation, and duplicate delivery.
            </Callout>
            <h2>Run it durable</h2>
            <p>
                The same module runs on PostgreSQL or SQL Server through EF
                Core: you choose the retention, run workers, and connect
                services over RabbitMQ.{' '}
                <a href={`${import.meta.env.BASE_URL}docs/running-durable/`}>
                    Read the Running durable guide
                </a>
                .
            </p>
            <h2>Generate documentation from the model</h2>
            <p>
                The Wyrd CLI exports AsyncAPI contracts and Mermaid process
                diagrams.{' '}
                <a href={`${import.meta.env.BASE_URL}docs/tool/`}>
                    Read the CLI guide
                </a>{' '}
                for commands you can run from the repository today.
            </p>
        </DocArticle>
    )
}
