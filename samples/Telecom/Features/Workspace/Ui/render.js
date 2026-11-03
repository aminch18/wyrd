import { pathPart } from "./api.js";

export function escapeHtml(value) {
    return String(value ?? "").replace(
        /[&<>"']/g,
        (character) =>
            ({
                "&": "&amp;",
                "<": "&lt;",
                ">": "&gt;",
                '"': "&quot;",
                "'": "&#39;",
            })[character],
    );
}

const e = escapeHtml;
const portPhases = [
    "Available",
    "Reserved",
    "Admitted",
    "Submitted",
    "Window confirmed",
    "Cancellation requested",
    "Cancelled",
    "Completed",
];

export function summary(cards) {
    const operational = cards.filter(
        (card) => card.progress.operational,
    ).length;
    const attention = cards.filter((card) => card.openIncidents > 0).length;
    return (
        metric("Total subscriptions", cards.length, "One journey per line") +
        metric(
            "In progress",
            cards.length - operational,
            "Waiting for verified facts",
        ) +
        metric("Operational", operational, "Ready for everyday use", "teal") +
        metric("Need attention", attention, "Open business incidents", "amber")
    );
}

function metric(label, value, description, tone = "") {
    return /* HTML */ `<article class="metric ${tone}">
        <span>${e(label)}</span><strong>${value}</strong
        ><small>${e(description)}</small>
    </article>`;
}

export function subscriptions(cards, selected, filter) {
    const matching = cards.filter(({ progress }) =>
        `${progress.subscriptionId} ${progress.msisdn ?? ""}`
            .toLowerCase()
            .includes(filter.toLowerCase()),
    );
    if (!matching.length)
        return '<p class="list-empty">No lines found. Contract a line to get started.</p>';
    return matching
        .map(
            ({
                progress: p,
                payment,
                openIncidents,
            }) => `<button type="button" class="line ${p.subscriptionId === selected ? "selected" : ""}" data-select="${e(p.subscriptionId)}" ${p.subscriptionId === selected ? 'aria-current="true"' : ""}>
    <span class="line-top"><strong>${e(p.msisdn ?? p.subscriptionId)}</strong><span class="status-dot ${p.operational ? "ready" : "pending"}"></span></span>
    <span class="line-id">${e(p.subscriptionId)}${openIncidents ? ` · ${openIncidents} incident${openIncidents === 1 ? "" : "s"}` : ""}</span>
    <span class="line-stage">${e(p.stage)}${payment?.billingStarted ? " · billing active" : ""}</span>
  </button>`,
        )
        .join("");
}

export function detail(workspace, mode, changes) {
    const { progress: p, payment, port, actions } = workspace;
    const name = p.msisdn ?? p.subscriptionId;
    const operational = p.operational;
    return /* HTML */ `<div class="detail-heading">
            <div>
                <p class="eyebrow">
                    ${e(p.subscriptionId)}
                    <span class="small-divider">/</span> CUSTOMER
                    ${e(p.customerId ?? "—")}
                </p>
                <h2>${e(name)}</h2>
            </div>
            <span class="badge ${operational ? "success" : "waiting"}"
                >${e(p.stage)}</span
            >
        </div>
        <div class="content-grid">
            <div class="journey-column">
                <article class="card journey">
                    <div class="section-heading">
                        <h3>Your connection journey</h3>
                        <span class="muted"
                            >${[
                                p.paid,
                                p.provisioned,
                                p.ported,
                                p.connected,
                                payment?.billingStarted,
                            ].filter(Boolean).length}
                            / 5 milestones</span
                        >
                    </div>
                    <ol class="milestones">
                        ${milestone(
                            "Initial payment",
                            p.paid,
                            payment?.receipt
                                ? `Receipt ${payment.receipt}`
                                : "Your initial payment is being processed.",
                        )}
                        ${milestone(
                            "eSIM provisioned",
                            p.provisioned,
                            p.profileId
                                ? `Profile ${p.profileId} · attempt ${p.attempt}`
                                : "An eSIM profile will be reserved for your line.",
                        )}
                        ${milestone(
                            "Number transferred",
                            p.ported,
                            portDescription(port),
                        )}
                        ${milestone(
                            "Connectivity verified",
                            p.connected,
                            p.connected
                                ? "The current profile has a matching network probe."
                                : "Waiting for the network to verify this profile and attempt.",
                        )}
                        ${milestone(
                            "Service & billing",
                            payment?.billingStarted,
                            payment?.billingStarted
                                ? "Service is operational. Billing has started."
                                : "Billing starts after all activation requirements are met.",
                        )}
                    </ol>
                </article>
                ${incidents(workspace.incidents, mode)}
                ${mode === "operations"
                    ? processes(workspace.processes)
                    : customerNote(p)}
            </div>
            <aside class="context-column">
                ${activationGate(p, payment)}
                <article class="card offer">
                    <p class="eyebrow">YOUR LINE</p>
                    <dl>
                        <div>
                            <dt>Initial payment</dt>
                            <dd>
                                ${p.initialAmount == null
                                    ? "—"
                                    : money(p.initialAmount)}
                            </dd>
                        </div>
                        <div>
                            <dt>eSIM profile</dt>
                            <dd>${e(p.profileId ?? "Pending")}</dd>
                        </div>
                        <div>
                            <dt>Profile attempt</dt>
                            <dd>${p.attempt || "—"}</dd>
                        </div>
                        <div>
                            <dt>Number request</dt>
                            <dd>${port ? `Epoch ${port.epoch}` : "Pending"}</dd>
                        </div>
                    </dl>
                </article>
                ${mode === "operations" ? operatorActions(actions) : ""}
            </aside>
        </div>
        ${mode === "operations"
            ? simulator(workspace) + inspector(workspace, changes)
            : ""}`;
}

function milestone(title, complete, description) {
    return /* HTML */ `<li
        class="milestone ${complete ? "complete" : "incomplete"}"
    >
        <span class="milestone-marker">${complete ? "✓" : ""}</span>
        <div>
            <h4>
                ${e(title)}${complete
                    ? '<span class="step-label">COMPLETE</span>'
                    : ""}
            </h4>
            <p>${e(description)}</p>
        </div>
    </li>`;
}

function portDescription(port) {
    if (!port) return "Your number reservation is being prepared.";
    if (port.phase === 7)
        return "The provider has confirmed that your number was transferred.";
    if (port.phase === 4)
        return /* HTML */ `Window: ${date(port.windowStart)} —
        ${date(port.windowEnd)}. Completion is still pending.`;
    return /* HTML */ `Portability: ${portPhases[port.phase] ?? "Pending"}.`;
}

function customerNote(p) {
    return /* HTML */ `<article class="customer-note">
        <span>${p.operational ? "✓" : "◷"}</span>
        <div>
            <h3>
                ${p.operational
                    ? "Everything is connected."
                    : "You can safely leave this page."}
            </h3>
            <p>
                ${p.operational
                    ? "Your current eSIM is verified and your service is active."
                    : "Your request and its progress are saved. We will continue processing while this page is closed."}
            </p>
        </div>
    </article>`;
}

function activationGate(p, payment) {
    const requirements = [
        ["Payment captured", p.paid],
        ["Port completed", p.ported],
        ["Current profile provisioned", p.provisioned],
        ["Matching connectivity proof", p.connected],
    ];
    const missing = requirements.filter(([, met]) => !met).length;
    return /* HTML */ `<article
        class="card activation-gate ${p.operational ? "gate-ready" : ""}"
    >
        <p class="eyebrow">SERVICE ACTIVATION</p>
        <h3>
            ${p.operational
                ? "Ready for your day."
                : `${missing} requirement${missing === 1 ? "" : "s"} to go.`}
        </h3>
        <ul>
            ${requirements
                .map(
                    ([label, met]) =>
                        `<li><span class="requirement-icon ${met ? "met" : ""}">${met ? "✓" : "○"}</span>${label}</li>`,
                )
                .join("")}
        </ul>
        <div class="billing-status">
            <span>Billing</span
            ><strong
                >${payment?.billingStarted ? "Active" : "Not started"}</strong
            >
        </div>
    </article>`;
}

function incidents(items, mode) {
    if (!items.length) return "";
    return /* HTML */ `<article class="card incidents">
        <div class="section-heading">
            <h3>Needs attention</h3>
            <span class="badge waiting">${items.length} open</span>
        </div>
        ${items
            .map(
                (item) =>
                    `<div class="incident"><div><strong>${e(item.kind)}</strong><p>Profile / review attempt ${item.attempt}</p></div>${mode === "operations" ? `<button class="secondary small" type="button" data-action="resolve-incident" data-kind="${e(item.kind)}" data-attempt="${item.attempt}">Resolve</button>` : ""}</div>`,
            )
            .join("")}
        <p class="form-help">
            An incident is a request for attention. Resolving it does not
            fabricate connectivity or reverse a completed port.
        </p>
    </article>`;
}

function processes(items) {
    return /* HTML */ `<article class="card processes">
        <div class="section-heading">
            <h3>Durable processes</h3>
            <span class="subtle-tag">ACTUAL RUNTIME HISTORY</span>
        </div>
        ${items
            .map(
                (process) =>
                    `<details class="process"><summary><span><strong>${e(process.Name ?? process.name)}</strong><small>${e(process.key)}</small></span><span class="badge ${process.status === "Completed" ? "success" : "waiting"}">${e(process.status ?? "Not started")}</span></summary><div class="process-history">${process.history.length ? process.history.map((entry) => `<div class="history-entry"><time>${date(entry.at)}</time><span><strong>${e(entry.kind)}</strong> ${e(shortType(entry.type))}<small>${e(entry.outcome ?? entry.reason ?? "")}${entry.attempt ? ` · attempt ${entry.attempt}` : ""}</small></span></div>`).join("") : '<p class="muted">No stored instance history yet.</p>'}</div></details>`,
            )
            .join("")}
        <p class="form-help">
            Awaiting means a correlated external fact is missing. Restarting a
            worker does not remove its durable wait.
        </p>
    </article>`;
}

function operatorActions(actions) {
    return /* HTML */ `<article class="card operator-actions">
        <p class="eyebrow">OPERATOR ACTIONS</p>
        <label
            >Replacement profile<input
                id="replacement-profile"
                maxlength="64"
                value="${e(actions.suggestedProfile)}" /></label
        ><button
            type="button"
            class="secondary full-width"
            data-action="replace-profile"
            ${actions.replaceProfile.enabled ? "" : "disabled"}
        >
            Assign attempt ${actions.nextAttempt}
        </button>
        <p class="action-reason">${e(actions.replaceProfile.reason)}</p>
        <button
            type="button"
            class="secondary full-width"
            data-action="cancel-port"
            ${actions.cancelPort.enabled ? "" : "disabled"}
        >
            Request port cancellation
        </button>
        <p class="action-reason">${e(actions.cancelPort.reason)}</p>
        <div class="action-pair">
            <button
                type="button"
                class="secondary small"
                data-action="reconcile"
            >
                Reconcile payment</button
            ><button
                type="button"
                class="secondary small"
                data-action="resume"
                ${actions.canResumePayment ? "" : "disabled"}
            >
                Resume payment
            </button>
        </div>
        <button type="button" class="text-button" data-action="flag-incident">
            Flag connectivity incident
        </button>
        <p class="form-help">
            Availability is advisory. Every submitted command is checked again
            against current domain state.
        </p>
    </article>`;
}

function simulator({ progress: p, port }) {
    const history = p.profileId
        ? `/histories/profiles/${pathPart(p.subscriptionId)}/${pathPart(p.profileId)}/${p.attempt}`
        : null;
    return /* HTML */ `<details class="card simulator">
        <summary>
            <span
                ><span class="eyebrow">LOCAL DEMO TOOLS</span
                ><strong>Act as the external provider</strong></span
            ><span class="subtle-tag">SIMULATOR</span>
        </summary>
        <p class="muted">
            These controls deliver trusted demo callbacks. Customer commands
            cannot declare a port completed or prove connectivity.
        </p>
        <div class="simulator-actions">
            <button
                type="button"
                class="secondary"
                data-action="confirm-port"
                ${port ? "" : "disabled"}
            >
                Confirm porting window</button
            ><button
                type="button"
                class="secondary"
                data-action="complete-port"
                ${port ? "" : "disabled"}
            >
                Complete port</button
            ><button
                type="button"
                class="secondary"
                data-action="confirm-cancellation"
                ${port?.cancellationRequested ? "" : "disabled"}
            >
                Acknowledge cancellation
            </button>
        </div>
        <div class="probe-form">
            <label
                >Probe profile<input
                    id="probe-profile"
                    value="${e(p.profileId ?? "")}"
                    maxlength="64" /></label
            ><label
                >Probe attempt<input
                    id="probe-attempt"
                    type="number"
                    min="1"
                    value="${p.attempt || 1}" /></label
            ><button
                type="button"
                class="primary"
                data-action="connectivity"
                ${p.profileId ? "" : "disabled"}
            >
                Verify connectivity
            </button>
        </div>
        <p class="form-help">
            Try port completion before connectivity: billing stays off. After
            replacement, send the old profile / attempt to see it leave current
            activation unchanged. The connectivity wait has a real 30-minute
            deadline.
        </p>
        ${history
            ? `<a class="text-link" href="${history}" target="_blank" rel="noopener">Export current profile history for Urd ↗</a>`
            : ""}
    </details>`;
}

function inspector(workspace, changes) {
    const changed = changes.length
        ? changes
              .map(
                  (change) =>
                      `<li><code>${e(change.field)}</code><span>${e(formatValue(change.before))} → ${e(formatValue(change.after))}</span></li>`,
              )
              .join("")
        : '<li class="muted">No projection changes observed in this browser session yet.</li>';
    return /* HTML */ `<details class="card inspector">
        <summary>
            <span
                ><span class="eyebrow">HOW THIS SCREEN IS BUILT</span
                ><strong>Views, BFF & rich domain model</strong></span
            ><span class="subtle-tag">INSPECT</span>
        </summary>
        <div class="architecture-flow">
            <span>Command</span>→<span>Domain policy</span
            >→<span>Event</span>→<span>Projection</span>→<span
                >BFF response</span
            >
        </div>
        <p>
            The customer screen composes persisted read models. The BFF adds
            process histories and advisory actions for operators. Domain state
            owns the final decision.
        </p>
        <h4>Projection changes observed on refresh</h4>
        <ul class="changes">
            ${changed}
        </ul>
        <div class="inspector-grid">
            <section>
                <h4>Persisted read models</h4>
                <p>
                    CustomerProgress + PaymentBillingStatus + PortabilityDetail
                </p>
                <pre>
${e(
                        JSON.stringify(
                            {
                                customerProgress: workspace.progress,
                                paymentBillingStatus: workspace.payment,
                                portabilityDetail: workspace.port,
                            },
                            null,
                            2,
                        ),
                    )}</pre
                >
            </section>
            <section>
                <h4>Domain state · diagnostic read</h4>
                <p>
                    Activation.Ready · CheckProfileAssignment ·
                    NumberPort.CheckCancellation · Payment.Capture
                </p>
                <pre>${e(JSON.stringify(workspace.model, null, 2))}</pre>
            </section>
        </div>
        <p class="form-help">
            The snapshots are independent reads, not an atomic view of every
            feature. Actions always revalidate. This small demo polls every two
            seconds; lists are unpaged.
        </p>
    </details>`;
}

export function projectionChanges(previous, next) {
    if (!previous) return [];
    const changes = [];
    for (const section of ["progress", "payment", "port"]) {
        for (const [key, value] of Object.entries(next[section] ?? {})) {
            const before = previous[section]?.[key];
            if (before !== value)
                changes.push({
                    field: `${section}.${key}`,
                    before,
                    after: value,
                });
        }
    }
    return changes;
}

function formatValue(value) {
    return value == null ? "—" : String(value);
}

function money(value) {
    return new Intl.NumberFormat("en-IE", {
        style: "currency",
        currency: "EUR",
    }).format(value);
}

function date(value) {
    return new Intl.DateTimeFormat(undefined, {
        dateStyle: "short",
        timeStyle: "medium",
    }).format(new Date(value));
}

function shortType(value) {
    return String(value ?? "")
        .split(".")
        .at(-1);
}
