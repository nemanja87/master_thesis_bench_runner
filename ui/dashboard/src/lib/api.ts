export const API_BASE = "/api";

async function ensureOk(response: Response, errorMessage: string): Promise<Response> {
  if (response.ok) {
    return response;
  }

  const text = await response.text().catch(() => "");
  let details = text.trim();

  if (text) {
    try {
      const data = JSON.parse(text) as { errors?: string[]; message?: string };
      if (Array.isArray(data.errors) && data.errors.length > 0) {
        details = data.errors.join(", ");
      } else if (data.message) {
        details = data.message;
      }
    } catch {
      // fall back to raw text
    }
  }

  const suffix = details ? ` ${details}` : "";
  throw new Error(`${errorMessage} HTTP ${response.status}${suffix}`);
}

export async function getHealth<T>(): Promise<T> {
  const res = await fetch(`${API_BASE}/healthz`);
  const ok = await ensureOk(res, "Failed to load health status.");
  return ok.json() as Promise<T>;
}

export async function listRuns<T>(): Promise<T> {
  const res = await fetch(`${API_BASE}/runs`);
  const ok = await ensureOk(res, "Failed to list runs.");
  return ok.json() as Promise<T>;
}

export async function getRun<T>(id: string): Promise<T> {
  const res = await fetch(`${API_BASE}/runs/${id}`);
  const ok = await ensureOk(res, `Failed to get run ${id}.`);
  return ok.json() as Promise<T>;
}

export async function startBench<T>(payload: unknown): Promise<T> {
  const res = await fetch(`${API_BASE}/benchrunner/run`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    body: JSON.stringify(payload),
  });

  const ok = await ensureOk(res, "Failed to start bench.");
  return ok.json() as Promise<T>;
}
