import type { ComponentType } from 'react'
import { Callout } from '../../components/common/Callout'
import { CodeBlock } from '../../components/common/CodeBlock'
import { TerminalInstall } from '../../components/common/TerminalInstall'
import { ANALYZER_RULES, SNIPPETS } from '../../data/docs'
import { GUARANTEES } from '../../data/marketing'
import { DocArticle } from './DocArticle'

function QuickStartDoc() {
  return <DocArticle eyebrow="Start here" title="Quick start" lead="Build and run your first deterministic process with the in-memory runtime.">
    <h2>Install the package</h2><p>Wyrd currently targets .NET 10 and C# 14. During alpha, install the latest prerelease package.</p><TerminalInstall />
    <h2>Describe the messages</h2><p>Commands are intentions. Domain events are internal facts. Integration events are explicit, versioned public contracts.</p><CodeBlock code={SNIPPETS.messages} title="Payments.cs" />
    <h2>Keep the decision pure</h2><p>A slice owns one command and decides against the current immutable state. Expected failure is a value, not an exception.</p><CodeBlock code={SNIPPETS.slice} title="ChargePaymentSlice.cs" />
    <h2>Compose the process</h2><p>Connect business steps, retries, compensation, and timeouts without exposing infrastructure to domain code.</p><CodeBlock code={SNIPPETS.fluentProcess} title="OrderFulfillment.cs" />
    <Callout title="Run the complete sample">Clone the repository and run <code>dotnet run --project samples/Shop.Console</code>. The walkthrough includes happy paths, gateway failures, timeouts, compensation, and duplicate delivery.</Callout>
  </DocArticle>
}

function OverviewDoc() {
  return <DocArticle eyebrow="Mental model" title="Overview & philosophy" lead="Wyrd treats determinism as the foundation, not an optional testing technique.">
    <h2>Pure core, framework-owned effects</h2><p>User-facing deciders and steps are pure functions. They cannot publish, persist, schedule, or reach into infrastructure. Wyrd performs those effects around the business model.</p>
    <div className="doc-principles">{GUARANTEES.map(({ title, copy, icon: Icon }) => <div key={title}><Icon size={20} /><h3>{title}</h3><p>{copy}</p></div>)}</div>
    <h2>One model, two syntaxes</h2><p>The fluent and pipe APIs build the same immutable <code>Process</code>. Choose the expression style your team reads most naturally.</p><CodeBlock code={SNIPPETS.pipeProcess} title="OrderFulfillment.cs" />
    <Callout title="The process is data">Because a process is data, the framework can render it, validate it, simulate its possible futures, and replay its recorded past.</Callout>
  </DocArticle>
}

function MessagesDoc() {
  return <DocArticle eyebrow="Domain model" title="Messages & slices" lead="Commands, facts, public contracts, and state each have a distinct job.">
    <h2>Say what a message means</h2><CodeBlock code={SNIPPETS.messages} title="Messages.cs" />
    <h2>Model state as a closed set</h2><p>Immutable union cases make impossible states unrepresentable, while <code>Evolve</code> remains an ordinary deterministic function.</p><CodeBlock code={SNIPPETS.stateUnion} title="Payment.cs" />
    <h2>Decide in one place</h2><p>A command has exactly one owning slice. The analyzer catches ambiguous ownership at compile time.</p><CodeBlock code={SNIPPETS.slice} title="ChargePaymentSlice.cs" />
  </DocArticle>
}

function ProcessesDoc() {
  return <DocArticle eyebrow="Orchestration" title="Declarative processes" lead="Model long-running business flows without giving domain code an infrastructure API.">
    <h2>Read it like the business describes it</h2><CodeBlock code={SNIPPETS.fluentProcess} title="OrderFulfillment.cs" />
    <h2>What the runtime guarantees</h2><ul><li>Each envelope is handled as one atomic change set.</li><li>Retries retain stable message identities and follow declared backoff.</li><li>Completed steps compensate in reverse order after rejection or exhausted faults.</li><li>Early awaited events are parked until the process reaches the matching await.</li><li>Timeouts use the runtime clock; tests advance it without sleeping.</li></ul>
    <h2>Observe the exact history</h2><CodeBlock code={SNIPPETS.journalOutput} language="text" title="Journal" />
  </DocArticle>
}

function ContractsDoc() {
  return <DocArticle eyebrow="Boundaries" title="Versioned contracts" lead="Refactor internal facts freely while keeping external promises explicit and reviewable.">
    <h2>Map internal facts deliberately</h2><CodeBlock code={SNIPPETS.publication} title="PaymentChargedPublication.cs" />
    <h2>Review the contract as text</h2><p>Every integration event is captured in <code>contracts.wyrd.txt</code>. Breaking changes fail the build; compatible additions receive an IDE code fix.</p><CodeBlock code={SNIPPETS.contractError} language="text" title="Contract diagnostic" />
    <h2>Analyzer rules</h2><div className="rule-table">{ANALYZER_RULES.map((rule) => <div key={rule.code}><code>{rule.code}</code><span className={`severity ${rule.severity.toLowerCase()}`}>{rule.severity}</span><p>{rule.description}</p></div>)}</div>
  </DocArticle>
}

function TestingDoc() {
  return <DocArticle eyebrow="Confidence" title="Testing & Native AOT" lead="Write business-readable scenarios and ship without runtime type discovery.">
    <h2>Given / When / Then for the domain</h2><p><code>Wyrd.Testing</code> exercises slices and projections through JSON round trips so tests stay honest about message boundaries.</p><CodeBlock code={SNIPPETS.testing} title="PaymentTests.cs" />
    <h2>Source-generated serialization</h2><p>Production code resolves registered names through your generated <code>JsonSerializerContext</code>; it does not depend on <code>Type.GetType</code> or runtime code generation.</p><CodeBlock code={SNIPPETS.aotJson} title="ShopJson.cs" />
    <Callout title="Native AOT is part of CI">The Shop console sample is published with Native AOT in CI, keeping trim safety a continuously verified property.</Callout>
  </DocArticle>
}

const ROADMAP = [
  ['v0.2', 'Distributed-shaped core', 'Available', 'Message-driven engine, atomic change sets, virtual time, projections, source-generated JSON, and Native AOT validation.'],
  ['v0.3', 'Skuld — simulation', 'In progress', 'Exhaustive failure simulation, state-aware steps, typed invokers, and the first process analyzers.'],
  ['v0.4', 'Urd — replay', 'In progress', 'The documentation tool already generates AsyncAPI and Mermaid. Instance capture and deterministic replay come next.'],
  ['v0.5', 'Verdandi — durable runtime', 'Planned', 'I/O integrations, durable state and scheduling, outbox/inbox storage, transports, and OpenTelemetry.'],
  ['v1.0', 'Production ready', 'Planned', 'Process versioning, end-to-end Native AOT, agent tooling, and complete framework documentation.'],
] as const

function RoadmapDoc() {
  return <DocArticle eyebrow="Direction" title="Roadmap" lead="Wyrd grows around three capabilities: simulate the future, replay the past, and run the present.">
    <div className="roadmap-list">{ROADMAP.map(([version, title, status, copy]) => <article key={version}><span>{version}</span><div><div className="roadmap-title"><h2>{title}</h2><em>{status}</em></div><p>{copy}</p></div></article>)}</div>
    <Callout title="The roadmap is a living contract">The repository roadmap is the source of truth. Features marked as planned here are not claims about the current package.</Callout>
  </DocArticle>
}

export const DOC_COMPONENTS: Record<string, ComponentType> = {
  'quick-start': QuickStartDoc,
  overview: OverviewDoc,
  messages: MessagesDoc,
  processes: ProcessesDoc,
  contracts: ContractsDoc,
  'testing-aot': TestingDoc,
  roadmap: RoadmapDoc,
}
