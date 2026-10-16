import { ArrowRight, BookOpen, Check, Github } from 'lucide-react'
import { GITHUB_URL, NUGET_URL } from '../app/links'
import { navigate } from '../app/navigation'
import { CodeBlock } from '../components/common/CodeBlock'
import { TerminalInstall } from '../components/common/TerminalInstall'
import { ThreadMark } from '../components/common/ThreadMark'
import { SNIPPETS } from '../data/docs'
import { GUARANTEES, NORNS } from '../data/marketing'

export function HomePage() {
  return (
    <main>
      <section className="hero section-shell">
        <div className="hero-copy reveal">
          <div className="eyebrow"><span /> Deterministic event-driven processes for .NET</div>
          <h1>Make distributed<br />business logic <em>boring.</em></h1>
          <p className="hero-lead">Pure functional DDD, declarative sagas, and versioned contracts—designed so the correct path is the one your code naturally takes.</p>
          <div className="hero-actions">
            <button className="button primary" type="button" onClick={() => navigate('docs', 'quick-start')}>Read the quick start <ArrowRight size={17} /></button>
            <a className="button secondary" href={GITHUB_URL} target="_blank" rel="noreferrer"><Github size={17} /> View on GitHub</a>
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
          <div><h2>Sagas fail in the spaces<br />between the happy paths.</h2><p>A duplicated message charges twice. A missing timeout waits forever. A failed compensation leaves stock reserved. It all compiles—and often passes the tests.</p></div>
          <div className="failure-list" aria-label="Common distributed failures"><span>duplicate delivery</span><span>partial failure</span><span>contract drift</span><span>late events</span><span>lost timeouts</span></div>
        </div>
      </section>

      <section className="section-shell principles-section">
        <div className="section-intro">
          <p className="section-number">02 / THE MODEL</p>
          <h2>Correct by construction.<br /><span>Readable by the business.</span></h2>
          <p>Wyrd draws a hard line between decisions and effects, then makes that line useful.</p>
        </div>
        <div className="guarantee-grid">
          {GUARANTEES.map(({ title, copy, icon: Icon }, index) => <article className="guarantee-card" key={title}><div className="card-index">0{index + 1}</div><Icon size={22} /><h3>{title}</h3><p>{copy}</p></article>)}
        </div>
      </section>

      <section className="code-story">
        <div className="section-shell code-story-grid">
          <div className="section-intro sticky-copy">
            <p className="section-number">03 / THE PROCESS</p><h2>Six lines that describe the business.</h2>
            <p>The process definition is immutable data. Steps are pure mappings from facts to intentions; Wyrd owns the messy mechanics around them.</p>
            <ul className="check-list"><li><Check size={16} /> Retries preserve the same message identity</li><li><Check size={16} /> Compensation runs in reverse order</li><li><Check size={16} /> Time advances on a virtual clock in tests</li></ul>
            <button className="text-link" type="button" onClick={() => navigate('docs', 'processes')}>Explore process modelling <ArrowRight size={16} /></button>
          </div>
          <CodeBlock code={SNIPPETS.fluentProcess} title="OrderFulfillment.cs" />
        </div>
      </section>

      <section className="section-shell norns-section">
        <div className="section-intro centered"><p className="section-number">04 / THE THREE NORNS</p><h2>Future. Past. Present.</h2><p>In Norse myth, wyrd—fate—is woven by three Norns. The framework follows the same shape.</p></div>
        <div className="norn-grid">
          {NORNS.map(({ name, tense, label, status, icon: Icon, copy }) => <article className="norn-card" key={name}><div className="norn-top"><Icon size={22} /><span>{status}</span></div><p className="norn-tense">{tense}</p><h3>{name}</h3><strong>{label}</strong><p>{copy}</p></article>)}
        </div>
      </section>

      <section className="contracts-section">
        <div className="section-shell contracts-grid">
          <div><p className="section-number">05 / PUBLIC CONTRACTS</p><h2>Break the contract.<br />Break the build.</h2><p>Integration events are public APIs. Wyrd snapshots their shape and explains what changed, why it is unsafe, and how to fix it.</p><div className="diagnostic"><span>WYRD0201</span><p><strong>Breaking change:</strong> field <code>Amount</code> changed from <code>decimal</code> to <code>double</code>.</p></div><button className="text-link" type="button" onClick={() => navigate('docs', 'contracts')}>Read about versioned contracts <ArrowRight size={16} /></button></div>
          <CodeBlock code={SNIPPETS.messages} title="Payments.cs" />
        </div>
      </section>

      <section className="section-shell final-cta">
        <ThreadMark compact />
        <div><p className="section-number">START WEAVING</p><h2>Event-driven that's<br />no longer weird.</h2><p>Wyrd is early-alpha software. Try the model, read the source, and help shape what comes next.</p><div className="hero-actions"><button className="button primary" type="button" onClick={() => navigate('docs', 'quick-start')}>Open the documentation <BookOpen size={17} /></button><a className="button secondary" href={NUGET_URL} target="_blank" rel="noreferrer">View NuGet package</a></div></div>
      </section>
    </main>
  )
}
