import { command, pathPart, query } from "./api.js";
import * as render from "./render.js";

function hashSelection() {
    try {
        return decodeURIComponent(location.hash.slice(1));
    } catch {
        return "";
    }
}

const state = {
    cards: [],
    selected: hashSelection(),
    workspace: null,
    changes: [],
    mode: "customer",
    filter: "",
    loading: false,
    mutating: false,
    renderPending: false,
};
const byId = (id) => document.getElementById(id);

function notify(message, error = false) {
    const notice = byId("notice");
    notice.textContent = message;
    notice.classList.toggle("error", error);
    notice.hidden = false;
}

function drawList() {
    byId("summary").innerHTML = render.summary(state.cards);
    byId("line-count").textContent = state.cards.length;
    byId("subscriptions").innerHTML = render.subscriptions(
        state.cards,
        state.selected,
        state.filter,
    );
}

function drawDetail() {
    if (!state.workspace) return;
    state.renderPending = false;
    const panel = byId("detail");
    // Preserve disclosure state as live data refreshes.
    const opened = new Set(
        [...panel.querySelectorAll("details[open]")].map(
            (element) => element.querySelector("summary strong")?.textContent,
        ),
    );
    panel.innerHTML = render.detail(state.workspace, state.mode, state.changes);
    for (const element of panel.querySelectorAll("details")) {
        element.open = opened.has(
            element.querySelector("summary strong")?.textContent,
        );
    }
}

async function refresh() {
    if (state.loading || state.mutating) return;
    state.loading = true;
    const selected = state.selected;
    try {
        const cards = await query("/bff/subscriptions");
        const cardsChanged =
            JSON.stringify(cards) !== JSON.stringify(state.cards);
        state.cards = cards;
        if (!state.selected && state.cards.length) {
            state.selected = state.cards[0].progress.subscriptionId;
            history.replaceState(null, "", `#${pathPart(state.selected)}`);
        }
        if (cardsChanged) drawList();
        if (state.selected) {
            const id = state.selected;
            const next = await query(
                `/bff/subscriptions/${pathPart(id)}`,
                true,
            );
            if (!next) {
                byId("refresh-status").textContent =
                    "Request accepted · waiting for its customer view";
                return;
            }
            // Ignore a response from a selection the user has already left.
            if (id === state.selected) {
                const previous =
                    state.workspace?.progress.subscriptionId === id
                        ? state.workspace
                        : null;
                state.changes = [
                    ...render.projectionChanges(previous, next),
                    ...state.changes,
                ].slice(0, 24);
                const changed =
                    JSON.stringify(state.workspace) !== JSON.stringify(next);
                state.workspace = next;
                // Do not overwrite an operator's form while they are typing.
                const editing =
                    byId("detail").contains(document.activeElement) &&
                    document.activeElement.matches("input, textarea, select");
                state.renderPending ||= changed;
                if (state.renderPending && !editing) drawDetail();
            }
        }
        byId("refresh-status").textContent =
            `Live · refreshed ${new Date().toLocaleTimeString()}`;
    } catch (error) {
        byId("refresh-status").textContent =
            "Connection interrupted · retrying automatically";
        notify(error.message, true);
    } finally {
        state.loading = false;
        if (selected && selected !== state.selected) void refresh();
    }
}

async function selectSubscription(id) {
    if (state.selected === id) return;
    state.selected = id;
    state.workspace = null;
    state.changes = [];
    history.replaceState(null, "", `#${pathPart(id)}`);
    drawList();
    byId("detail").innerHTML =
        '<div class="empty-state"><h2>Loading your line…</h2></div>';
    await refresh();
}

byId("subscriptions").addEventListener("click", (event) => {
    const line = event.target.closest("[data-select]");
    if (line) void selectSubscription(line.dataset.select);
});

byId("search").addEventListener("input", (event) => {
    state.filter = event.target.value;
    drawList();
});

for (const button of document.querySelectorAll("[data-mode]")) {
    button.addEventListener("click", () => {
        state.mode = button.dataset.mode;
        for (const item of document.querySelectorAll("[data-mode]")) {
            const active = item.dataset.mode === state.mode;
            item.classList.toggle("active", active);
            item.setAttribute("aria-pressed", String(active));
        }
        drawDetail();
    });
}

byId("new-subscription").addEventListener("click", () => {
    const suffix = crypto.randomUUID().slice(0, 8);
    const form = byId("contract-form");
    form.elements.subscriptionId.value = `S-${suffix}`;
    form.elements.customerId.value = `C-${suffix}`;
    form.elements.profileId.value = `P-${suffix}`;
    const digits = String(
        crypto.getRandomValues(new Uint32Array(1))[0] % 1000000,
    ).padStart(6, "0");
    form.elements.msisdn.value = `+34600${digits}`;
    byId("contract-error").textContent = "";
    byId("contract-dialog").showModal();
});

byId("close-contract").addEventListener("click", () =>
    byId("contract-dialog").close(),
);
byId("contract-form").addEventListener("submit", async (event) => {
    event.preventDefault();
    const form = event.currentTarget;
    const submit = form.querySelector('[type="submit"]');
    const fields = Object.fromEntries(new FormData(form));
    const offer = { ...fields, epoch: Number(fields.epoch), initialAmount: 25 };
    submit.disabled = true;
    try {
        await command("/subscriptions", offer);
        byId("contract-dialog").close();
        notify(
            "Subscription accepted. Its saved progress will appear as the workers process the request.",
        );
        // A durable API accepts before projections catch up. Poll until the new card appears.
        state.cards = await query("/bff/subscriptions");
        drawList();
        state.selected = offer.subscriptionId;
        state.workspace = null;
        state.changes = [];
        history.replaceState(null, "", `#${pathPart(state.selected)}`);
        byId("detail").innerHTML =
            '<div class="empty-state"><h2>Request accepted.</h2><p>Waiting for the saved customer view…</p></div>';
        await refresh();
    } catch (error) {
        byId("contract-error").textContent = error.message;
    } finally {
        submit.disabled = false;
    }
});

const actions = {
    "replace-profile": (workspace) =>
        command(
            `/subscriptions/${pathPart(workspace.progress.subscriptionId)}/profiles/${pathPart(byId("replacement-profile").value)}/attempts/${workspace.actions.nextAttempt}`,
        ),
    "cancel-port": (workspace) =>
        command(
            `/subscriptions/${pathPart(workspace.progress.subscriptionId)}/cancel-port`,
        ),
    reconcile: (workspace) =>
        command(
            `/subscriptions/${pathPart(workspace.progress.subscriptionId)}/reconcile/${workspace.actions.nextReview}`,
        ),
    resume: (workspace) =>
        command(
            `/operators/payment/${pathPart(workspace.progress.subscriptionId)}/resume`,
        ),
    "complete-port": (workspace) =>
        command("/webhooks/mnp/completed", portNotice(workspace)),
    "confirm-port": (workspace) => {
        const start = new Date(Date.now() + 3600000);
        const end = new Date(start.getTime() + 7200000);
        return command("/webhooks/mnp/confirmed", {
            ...portNotice(workspace),
            windowStart: start.toISOString(),
            windowEnd: end.toISOString(),
        });
    },
    "confirm-cancellation": (workspace) =>
        command("/webhooks/mnp/cancelled", portNotice(workspace)),
    connectivity: (workspace) =>
        command("/webhooks/connectivity", {
            subscriptionId: workspace.progress.subscriptionId,
            profileId: byId("probe-profile").value,
            attempt: Number(byId("probe-attempt").value),
        }),
    "flag-incident": (workspace) =>
        command(
            `/incidents/${pathPart(workspace.progress.subscriptionId)}/connectivity/${workspace.progress.attempt}/open`,
        ),
    "resolve-incident": (workspace, button) =>
        command(
            `/incidents/${pathPart(workspace.progress.subscriptionId)}/${pathPart(button.dataset.kind)}/${button.dataset.attempt}/resolve`,
        ),
};

function portNotice(workspace) {
    return {
        subscriptionId: workspace.progress.subscriptionId,
        msisdn: workspace.port.msisdn,
        epoch: workspace.port.epoch,
    };
}

byId("detail").addEventListener("click", async (event) => {
    const button = event.target.closest("[data-action]");
    if (!button || button.disabled || state.mutating || !state.workspace)
        return;
    const action = actions[button.dataset.action];
    if (!action) return;
    state.mutating = true;
    button.disabled = true;
    try {
        await action(state.workspace, button);
        notify("Request accepted. Watching persisted views for the result…");
    } catch (error) {
        notify(error.message, true);
    } finally {
        state.mutating = false;
        button.disabled = false;
        await refresh();
    }
});

window.addEventListener(
    "hashchange",
    () => void selectSubscription(hashSelection()),
);
await refresh();
setInterval(() => void refresh(), 2000);
