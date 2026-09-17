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

export async function createMigrationPlan(assessment) {
  const result = await apiRequest("/api/migration-plans", {
    method: "POST",
    body: JSON.stringify(assessment)
  });

  if (!result || typeof result !== "object" || !result.plan || typeof result.plan !== "object") {
    throw new Error("The migration planner returned an invalid response.");
  }

  return result;
}

function normalizeError(payload, status) {
  const errorText =
    typeof payload === "string" ? payload : JSON.stringify(payload);
  if (
    status === 429 ||
    /RateLimitReached|exceeded rate limit|Status:\s*429/i.test(errorText)
  ) {
    return "Phi-4 is temporarily rate-limited. Wait a few seconds, then retry plan generation.";
  }
  if (typeof payload === "string") return payload;
  if (payload?.error) {
    const details = Array.isArray(payload.details)
      ? ` ${payload.details.join(" ")}`
      : "";
    return `${payload.error}${details}`;
  }
  if (payload?.title) return payload.detail || payload.title;
  return `The service returned HTTP ${status}.`;
}
