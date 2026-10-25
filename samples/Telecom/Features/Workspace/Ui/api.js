export async function query(path, allowMissing = false) {
    const response = await fetch(path, {
        headers: { Accept: "application/json" },
    });
    if (response.status === 404 && allowMissing) return null;
    if (!response.ok)
        throw new Error(`Could not load the workspace (${response.status}).`);
    return response.json();
}

export async function command(path, body) {
    const options = { method: "POST", headers: { Accept: "application/json" } };
    if (body !== undefined) {
        options.headers["Content-Type"] = "application/json";
        options.body = JSON.stringify(body);
    }
    const response = await fetch(path, options);
    if (!response.ok) {
        const detail = await response.json().catch(() => null);
        const rule = detail?.error ?? detail?.title ?? "Request failed";
        throw new Error(
            `${rule} (${response.status}). The command was not accepted.`,
        );
    }
}

export function pathPart(value) {
    return encodeURIComponent(value);
}
