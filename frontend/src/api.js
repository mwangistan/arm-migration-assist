const apiBaseUrl = (import.meta.env.VITE_API_BASE_URL || "").replace(/\/+$/, "");

export async function apiRequest(path, options = {}) {
  const response = await fetch(`${apiBaseUrl}${path}`, {
    ...options,
    headers: {
      "Content-Type": "application/json",
      ...options.headers
    }
  });

  const contentType = response.headers.get("content-type") || "";
  const payload = contentType.includes("json")
    ? await response.json()
    : await response.text();

  if (!response.ok) {
    throw new Error(normalizeError(payload, response.status));
  }

  return payload;
}

export function assessRepository(repoUrl, target) {
  return apiRequest("/assess", {
    method: "POST",
    body: JSON.stringify({ repoUrl, target })
  });
}

function normalizeError(payload, status) {
  if (typeof payload === "string") return payload;
  if (payload?.error) {
    const details = Array.isArray(payload.details)
      ? ` ${payload.details.join(" ")}`
      : "";
    return `${payload.error}${details}`;
  }
  if (payload?.title) return payload.detail || payload.title;
  return `The assessment service returned HTTP ${status}.`;
}
