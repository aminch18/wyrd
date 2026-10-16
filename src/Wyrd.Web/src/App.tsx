import { useEffect, useMemo, useState } from 'react'
import {
  ArrowRight,
  BookOpen,
  Check,
  ChevronRight,
  Clock3,
  Code2,
  Copy,
  Github,
  Menu,
  Network,
  PackageCheck,
  Route,
  ShieldCheck,
  Sparkles,
  X,
} from 'lucide-react'
import { CodeBlock } from './components/CodeBlock'
import { ANALYZER_RULES, DOC_SECTIONS, SNIPPETS } from './data/docs'

const GITHUB_URL = 'https://github.com/aminch18/wyrd'
const NUGET_URL = 'https://www.nuget.org/packages/Wyrd'

type Page = 'home' | 'docs'

function pageFromHash(): Page {
  return window.location.hash.startsWith('#/docs') ? 'docs' : 'home'
}

function navigate(page: Page, section?: string) {
  const suffix = page === 'docs' && section ? `/${section}` : ''
  window.location.hash = page === 'home' ? '#/' : `#/docs${suffix}`
}

function Brand() {
  return (
    <button className="brand" type="button" onClick={() => navigate('home')} aria-label="Wyrd home">
      <span className="brand-mark" aria-hidden="true">
        <span />
        <span />
        <span />
      </span>
      <span>wyrd</span>
    </button>
  )
}

function Header({ page }: { page: Page }) {
  const [open, setOpen] = useState(false)

  const go = (target: Page, section?: string) => {
    navigate(target, section)
    setOpen(false)
  }

  return (
    <header className="site-header">
      <div className="nav-shell">
        <Brand />
        <button
          className="menu-button"
          type="button"
          onClick={() => setOpen((value) => !value)}
          aria-label="Toggle navigation"
          aria-expanded={open}
        >
          {open ? <X /> : <Menu />}
        </button>
        <nav className={open ? 'nav-links nav-links-open' : 'nav-links'} aria-label="Main navigation">
          <button className={page === 'home' ? 'active' : ''} type="button" onClick={() => go('home')}>
            Why Wyrd
          </button>
          <button className={page === 'docs' ? 'active' : ''} type="button" onClick={() => go('docs')}>
            Documentation
          </button>
          <a href={GITHUB_URL} target="_blank" rel="noreferrer">
            GitHub <Github size={16} />
          </a>
          <button className="nav-cta" type="button" onClick={() => go('docs', 'quick-start')}>
            Get started <ArrowRight size={15} />
          </button>
        </nav>
      </div>
    </header>
  )
}

function ThreadMark({ compact = false }: { compact?: boolean }) {
  return (
    <div className={compact ? 'thread-mark compact' : 'thread-mark'} aria-hidden="true">
      <svg viewBox="0 0 680 420" role="img">
        <defs>
          <linearGradient id="thread-gradient" x1="90" y1="60" x2="590" y2="360" gradientUnits="userSpaceOnUse">
            <stop stopColor="#67e8f9" />
            <stop offset="0.48" stopColor="#818cf8" />
            <stop offset="1" stopColor="#c084fc" />
          </linearGradient>
          <filter id="thread-glow" x="-30%" y="-30%" width="160%" height="160%">
            <feGaussianBlur stdDeviation="12" result="blur" />
            <feMerge><feMergeNode in="blur" /><feMergeNode in="SourceGraphic" /></feMerge>
          </filter>
        </defs>
        <path className="thread-faint" d="M58 335C154 137 222 116 300 246C354 336 437 350 618 82" />
        <path className="thread-faint" d="M66 90C175 305 252 310 337 188C398 102 478 96 615 325" />
        <path className="thread-main" d="M66 90C175 305 252 310 337 188C398 102 478 96 615 325" />
        <path className="thread-main delayed" d="M58 335C154 137 222 116 300 246C354 336 437 350 618 82" />
        <circle cx="66" cy="90" r="7" />
        <circle cx="337" cy="188" r="7" />
        <circle cx="615" cy="325" r="7" />
      </svg>
    </div>
  )
}

function TerminalInstall() {
  const [copied, setCopied] = useState(false)
  const command = 'dotnet add package Wyrd --prerelease'

  const copy = async () => {
    await navigator.clipboard.writeText(command)
    setCopied(true)
    window.setTimeout(() => setCopied(false), 1600)
  }

  return (
    <div className="install-command">
      <span aria-hidden="true">$</span>
      <code>{command}</code>
      <button type="button" onClick={copy} aria-label="Copy install command">
        {copied ? <Check size={16} /> : <Copy size={16} />}
      </button>
    </div>
  )
}

const GUARANTEES = [
  ['Pure decisions', 'Business rules stay as ordinary functions. No database, publisher, clock, or hidden I/O.', Code2],
  ['Effects owned by Wyrd', 'Retries, timeouts, idempotency, compensation, and delivery belong to the framework.', ShieldCheck],
  ['Processes are data', 'The same model can be run, drawn, inspected, simulated, and eventually replayed.', Route],
  ['Contracts that resist drift', 'Roslyn analyzers catch broken public events while the change is still in your editor.', PackageCheck],
] as const

const NORN_DATA = [
  {
    name: 'Skuld',
    tense: 'what will be',
    label: 'Simulate',
    status: 'In progress',
    icon: Sparkles,
    copy: 'Explore failure, retry, duplicate, reordering, and timeout paths before production finds them.',
  },
  {
    name: 'Urd',
    tense: 'what was',
    label: 'Replay',
    status: 'Planned',
    icon: Clock3,
    copy: 'Turn the exact history of a production process into a deterministic local test.',
  },
  {
    name: 'Verdandi',
    tense: 'what is',
    label: 'Run',
    status: 'Core available',
    icon: Network,
    copy: 'Execute one message and commit state, events, inbox, outbox, and timers atomically.',
  },
] as const

function HomePage() {
  return (
    <main>
      <section className="hero section-shell">
        <div className="hero-copy reveal">
          <div className="eyebrow"><span /> Deterministic event-driven processes for .NET</div>
          <h1>Make distributed<br />business logic <em>boring.</em></h1>
          <p className="hero-lead">
            Pure functional DDD, declarative sagas, and versioned contracts—designed so the correct path is the one your code naturally takes.
          </p>
          <div className="hero-actions">
            <button className="button primary" type="button" onClick={() => navigate('docs', 'quick-start')}>
              Read the quick start <ArrowRight size={17} />
            </button>
            <a className="button secondary" href={GITHUB_URL} target="_blank" rel="noreferrer">
              <Github size={17} /> View on GitHub
            </a>
          </div>
          <TerminalInstall />
          <div className="alpha-note"><span>v0.2 alpha</span> · built for .NET 10 and C# 14</div>
        </div>
        <div className="hero-visual reveal delay-one">
          <ThreadMark />
          <div className="hero-code-float">
            <div><span className="code-purple">Process</span>.<span className="code-cyan">StartOn</span>&lt;<span className="code-gold">OrderPlaced</span>&gt;()</div>
            <div className="indent">.<span className="code-cyan">Then</span>(Stock.Reserve)</div>
            <div className="indent">.<span className="code-cyan">Then</span>(Payments.Charge).<span className="code-cyan">Retry</span>(3.Times)</div>
            <div className="indent">.<span className="code-cyan">Await</span>&lt;<span className="code-gold">PaymentConfirmed</span>&gt;()</div>
          </div>
        </div>
      </section>

      <section className="problem-band">
        <div className="section-shell statement-grid">
          <p className="section-number">01 / THE PROBLEM</p>
          <div>
            <h2>Sagas fail in the spaces<br />between the happy paths.</h2>
            <p>
              A duplicated message charges twice. A missing timeout waits forever. A failed compensation leaves stock reserved. It all compiles—and often passes the tests.
            </p>
          </div>
          <div className="failure-list" aria-label="Common distributed failures">
            <span>duplicate delivery</span>
            <span>partial failure</span>
            <span>contract drift</span>
            <span>late events</span>
            <span>lost timeouts</span>
          </div>
        </div>
      </section>

      <section className="section-shell principles-section">
        <div className="section-intro">
          <p className="section-number">02 / THE MODEL</p>
          <h2>Correct by construction.<br /><span>Readable by the business.</span></h2>
          <p>Wyrd draws a hard line between decisions and effects, then makes that line useful.</p>
        </div>
        <div className="guarantee-grid">
          {GUARANTEES.map(([title, copy, Icon], index) => (
            <article className="guarantee-card" key={title}>
              <div className="card-index">0{index + 1}</div>
              <Icon size={22} />
              <h3>{title}</h3>
              <p>{copy}</p>
            </article>
          ))}
        </div>
      </section>

      <section className="code-story">
        <div className="section-shell code-story-grid">
          <div className="section-intro sticky-copy">
            <p className="section-number">03 / THE PROCESS</p>
            <h2>Six lines that describe the business.</h2>
            <p>
              The process definition is immutable data. Steps are pure mappings from facts to intentions; Wyrd owns the messy mechanics around them.
            </p>
            <ul className="check-list">
              <li><Check size={16} /> Retries preserve the same message identity</li>
              <li><Check size={16} /> Compensation runs in reverse order</li>
              <li><Check size={16} /> Time advances on a virtual clock in tests</li>
            </ul>
            <button className="text-link" type="button" onClick={() => navigate('docs', 'processes')}>
              Explore process modelling <ArrowRight size={16} />
            </button>
          </div>
          <CodeBlock code={SNIPPETS.fluentProcess} title="OrderFulfillment.cs" />
        </div>
      </section>

      <section className="section-shell norns-section">
        <div className="section-intro centered">
          <p className="section-number">04 / THE THREE NORNS</p>
          <h2>Future. Past. Present.</h2>
          <p>In Norse myth, wyrd—fate—is woven by three Norns. The framework follows the same shape.</p>
        </div>
        <div className="norn-grid">
          {NORN_DATA.map(({ name, tense, label, status, icon: Icon, copy }) => (
            <article className="norn-card" key={name}>
              <div className="norn-top"><Icon size={22} /><span>{status}</span></div>
              <p className="norn-tense">{tense}</p>
              <h3>{name}</h3>
              <strong>{label}</strong>
              <p>{copy}</p>
            </article>
          ))}
        </div>
      </section>

      <section className="contracts-section">
        <div className="section-shell contracts-grid">
          <div>
            <p className="section-number">05 / PUBLIC CONTRACTS</p>
            <h2>Break the contract.<br />Break the build.</h2>
            <p>
              Integration events are public APIs. Wyrd snapshots their shape and explains what changed, why it is unsafe, and how to fix it.
            </p>
            <div className="diagnostic">
              <span>WYRD0201</span>
              <p><strong>Breaking change:</strong> field <code>Amount</code> changed from <code>decimal</code> to <code>double</code>.</p>
            </div>
            <button className="text-link" type="button" onClick={() => navigate('docs', 'contracts')}>
              Read about versioned contracts <ArrowRight size={16} />
            </button>
          </div>
          <CodeBlock code={SNIPPETS.messages} title="Payments.cs" />
        </div>
      </section>

      <section className="section-shell final-cta">
        <ThreadMark compact />
        <div>
          <p className="section-number">START WEAVING</p>
          <h2>Event-driven that's<br />no longer weird.</h2>
          <p>Wyrd is early-alpha software. Try the model, read the source, and help shape what comes next.</p>
          <div className="hero-actions">
            <button className="button primary" type="button" onClick={() => navigate('docs', 'quick-start')}>
              Open the documentation <BookOpen size={17} />
            </button>
            <a className="button secondary" href={NUGET_URL} target="_blank" rel="noreferrer">View NuGet package</a>
          </div>
        </div>
      </section>
    </main>
  )
}

function DocsNav({ current }: { current: string }) {
  const items = useMemo(() => [
    { id: 'quick-start', title: 'Quick start', subtitle: 'Install and run the sample.' },
    ...DOC_SECTIONS,
  ], [])

  return (
    <aside className="docs-sidebar">
      <p>Documentation</p>
      <nav aria-label="Documentation sections">
        {items.map((item) => (
          <button
            className={current === item.id ? 'current' : ''}
            type="button"
            key={item.id}
            onClick={() => navigate('docs', item.id)}
          >
            <span>{item.title}</span>
            <ChevronRight size={14} />
          </button>
        ))}
      </nav>
      <div className="docs-side-note">
        <span>Early alpha</span>
        <p>The APIs are tested, but the project is still evolving quickly.</p>
      </div>
    </aside>
  )
}

function QuickStartDoc() {
  return (
    <DocArticle eyebrow="Start here" title="Quick start" lead="Build and run your first deterministic process with the in-memory runtime.">
      <h2>Install the package</h2>
      <p>Wyrd currently targets .NET 10 and C# 14. During alpha, install the latest prerelease package.</p>
      <TerminalInstall />
      <h2>Describe the messages</h2>
      <p>Commands are intentions. Domain events are internal facts. Integration events are explicit, versioned public contracts.</p>
      <CodeBlock code={SNIPPETS.messages} title="Payments.cs" />
      <h2>Keep the decision pure</h2>
      <p>A slice owns one command and decides against the current immutable state. Expected failure is a value, not an exception.</p>
      <CodeBlock code={SNIPPETS.slice} title="ChargePaymentSlice.cs" />
      <h2>Compose the process</h2>
      <p>Connect business steps, retries, compensation, and timeouts without exposing infrastructure to domain code.</p>
      <CodeBlock code={SNIPPETS.fluentProcess} title="OrderFulfillment.cs" />
      <Callout title="Run the complete sample">
        Clone the repository and run <code>dotnet run --project samples/Shop.Console</code>. The walkthrough includes happy paths, gateway failures, timeouts, compensation, and duplicate delivery.
      </Callout>
    </DocArticle>
  )
}

function OverviewDoc() {
  return (
    <DocArticle eyebrow="Mental model" title="Overview & philosophy" lead="Wyrd treats determinism as the foundation, not an optional testing technique.">
      <h2>Pure core, framework-owned effects</h2>
      <p>User-facing deciders and steps are pure functions. They cannot publish, persist, schedule, or reach into infrastructure. Wyrd performs those effects around the business model.</p>
      <div className="doc-principles">
        {GUARANTEES.map(([title, copy, Icon]) => <div key={title}><Icon size={20} /><h3>{title}</h3><p>{copy}</p></div>)}
      </div>
      <h2>One model, two syntaxes</h2>
      <p>The fluent and pipe APIs build the same immutable <code>Process</code>. Choose the expression style your team reads most naturally.</p>
      <CodeBlock code={SNIPPETS.pipeProcess} title="OrderFulfillment.cs" />
      <Callout title="The process is data">Because a process is data, the framework can render it, validate it, simulate its possible futures, and replay its recorded past.</Callout>
    </DocArticle>
  )
}

function MessagesDoc() {
  return (
    <DocArticle eyebrow="Domain model" title="Messages & slices" lead="Commands, facts, public contracts, and state each have a distinct job.">
      <h2>Say what a message means</h2>
      <CodeBlock code={SNIPPETS.messages} title="Messages.cs" />
      <h2>Model state as a closed set</h2>
      <p>Immutable union cases make impossible states unrepresentable, while <code>Evolve</code> remains an ordinary deterministic function.</p>
      <CodeBlock code={SNIPPETS.stateUnion} title="Payment.cs" />
      <h2>Decide in one place</h2>
      <p>A command has exactly one owning slice. The analyzer catches ambiguous ownership at compile time.</p>
      <CodeBlock code={SNIPPETS.slice} title="ChargePaymentSlice.cs" />
    </DocArticle>
  )
}

function ProcessesDoc() {
  return (
    <DocArticle eyebrow="Orchestration" title="Declarative processes" lead="Model long-running business flows without giving domain code an infrastructure API.">
      <h2>Read it like the business describes it</h2>
      <CodeBlock code={SNIPPETS.fluentProcess} title="OrderFulfillment.cs" />
      <h2>What the runtime guarantees</h2>
      <ul>
        <li>Each envelope is handled as one atomic change set.</li>
        <li>Retries retain stable message identities and follow declared backoff.</li>
        <li>Completed steps compensate in reverse order after rejection or exhausted faults.</li>
        <li>Early awaited events are parked until the process reaches the matching await.</li>
        <li>Timeouts use the runtime clock; tests advance it without sleeping.</li>
      </ul>
      <h2>Observe the exact history</h2>
      <CodeBlock code={SNIPPETS.journalOutput} language="text" title="Journal" />
    </DocArticle>
  )
}

function ContractsDoc() {
  return (
    <DocArticle eyebrow="Boundaries" title="Versioned contracts" lead="Refactor internal facts freely while keeping external promises explicit and reviewable.">
      <h2>Map internal facts deliberately</h2>
      <CodeBlock code={SNIPPETS.publication} title="PaymentChargedPublication.cs" />
      <h2>Review the contract as text</h2>
      <p>Every integration event is captured in <code>contracts.wyrd.txt</code>. Breaking changes fail the build; compatible additions receive an IDE code fix.</p>
      <CodeBlock code={SNIPPETS.contractError} language="text" title="Contract diagnostic" />
      <h2>Analyzer rules</h2>
      <div className="rule-table">
        {ANALYZER_RULES.map((rule) => <div key={rule.code}><code>{rule.code}</code><span className={`severity ${rule.severity.toLowerCase()}`}>{rule.severity}</span><p>{rule.description}</p></div>)}
      </div>
    </DocArticle>
  )
}

function TestingDoc() {
  return (
    <DocArticle eyebrow="Confidence" title="Testing & Native AOT" lead="Write business-readable scenarios and ship without runtime type discovery.">
      <h2>Given / When / Then for the domain</h2>
      <p><code>Wyrd.Testing</code> exercises slices and projections through JSON round trips so tests stay honest about message boundaries.</p>
      <CodeBlock code={SNIPPETS.testing} title="PaymentTests.cs" />
      <h2>Source-generated serialization</h2>
      <p>Production code resolves registered names through your generated <code>JsonSerializerContext</code>; it does not depend on <code>Type.GetType</code> or runtime code generation.</p>
      <CodeBlock code={SNIPPETS.aotJson} title="ShopJson.cs" />
      <Callout title="Native AOT is part of CI">The Shop console sample is published with Native AOT in CI, keeping trim safety a continuously verified property.</Callout>
    </DocArticle>
  )
}

function RoadmapDoc() {
  const phases = [
    ['v0.2', 'Distributed-shaped core', 'Available', 'Message-driven engine, atomic change sets, virtual time, projections, source-generated JSON, and Native AOT validation.'],
    ['v0.3', 'Skuld — simulation', 'In progress', 'Exhaustive failure simulation, state-aware steps, typed invokers, and the first process analyzers.'],
    ['v0.4', 'Urd — replay', 'In progress', 'The documentation tool already generates AsyncAPI and Mermaid. Instance capture and deterministic replay come next.'],
    ['v0.5', 'Verdandi — durable runtime', 'Planned', 'I/O integrations, durable state and scheduling, outbox/inbox storage, transports, and OpenTelemetry.'],
    ['v1.0', 'Production ready', 'Planned', 'Process versioning, end-to-end Native AOT, agent tooling, and complete framework documentation.'],
  ]

  return (
    <DocArticle eyebrow="Direction" title="Roadmap" lead="Wyrd grows around three capabilities: simulate the future, replay the past, and run the present.">
      <div className="roadmap-list">
        {phases.map(([version, title, status, copy]) => <article key={version}><span>{version}</span><div><div className="roadmap-title"><h2>{title}</h2><em>{status}</em></div><p>{copy}</p></div></article>)}
      </div>
      <Callout title="The roadmap is a living contract">The repository roadmap is the source of truth. Features marked as planned here are not claims about the current package.</Callout>
    </DocArticle>
  )
}

function DocArticle({ eyebrow, title, lead, children }: { eyebrow: string; title: string; lead: string; children: React.ReactNode }) {
  return (
    <article className="doc-article">
      <p className="doc-eyebrow">{eyebrow}</p>
      <h1>{title}</h1>
      <p className="doc-lead">{lead}</p>
      <div className="doc-divider" />
      {children}
      <div className="edit-link"><Github size={15} /> Documentation follows the repository source of truth.</div>
    </article>
  )
}

function Callout({ title, children }: { title: string; children: React.ReactNode }) {
  return <aside className="callout"><Sparkles size={18} /><div><strong>{title}</strong><p>{children}</p></div></aside>
}

const DOC_COMPONENTS: Record<string, () => React.ReactElement> = {
  'quick-start': QuickStartDoc,
  overview: OverviewDoc,
  messages: MessagesDoc,
  processes: ProcessesDoc,
  contracts: ContractsDoc,
  'testing-aot': TestingDoc,
  roadmap: RoadmapDoc,
}

function DocsPage() {
  const section = window.location.hash.split('/')[2] || 'quick-start'
  const current = DOC_COMPONENTS[section] ? section : 'quick-start'
  const Content = DOC_COMPONENTS[current]

  useEffect(() => {
    window.scrollTo({ top: 0 })
  }, [current])

  return (
    <main className="docs-page">
      <div className="docs-shell">
        <DocsNav current={current} />
        <Content />
        <aside className="docs-outline">
          <p>On this page</p>
          <span>{current.replace('-', ' ')}</span>
          <a href={GITHUB_URL} target="_blank" rel="noreferrer"><Github size={14} /> View source</a>
        </aside>
      </div>
    </main>
  )
}

function Footer() {
  return (
    <footer>
      <div className="section-shell footer-inner">
        <div><Brand /><p>Deterministic event-driven processes for .NET.</p></div>
        <div className="footer-links">
          <button type="button" onClick={() => navigate('docs')}>Documentation</button>
          <a href={GITHUB_URL} target="_blank" rel="noreferrer">GitHub</a>
          <a href={NUGET_URL} target="_blank" rel="noreferrer">NuGet</a>
        </div>
        <p className="footer-note">Early alpha · MIT licensed</p>
      </div>
    </footer>
  )
}

export function App() {
  const [route, setRoute] = useState(window.location.hash)
  const page = pageFromHash()

  useEffect(() => {
    const onHashChange = () => setRoute(window.location.hash)
    window.addEventListener('hashchange', onHashChange)
    if (!window.location.hash) navigate('home')
    return () => window.removeEventListener('hashchange', onHashChange)
  }, [])

  useEffect(() => {
    document.title = page === 'docs'
      ? 'Documentation — Wyrd'
      : 'Wyrd — Deterministic Event-Driven Processes for .NET'
  }, [page, route])

  return (
    <div className="app">
      <Header page={page} />
      {page === 'home' ? <HomePage /> : <DocsPage />}
      <Footer />
    </div>
  )
}
