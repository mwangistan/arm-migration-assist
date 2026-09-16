(() => {
  "use strict";

  const state = {
    assessment: null,
    target: "Arm64Native",
    dependencyFilter: { search: "", status: "all" },
    severity: "all",
    loadingTimer: null
  };

  const byId = id => document.getElementById(id);
  const views = ["connect-view", "loading-view", "error-view", "dashboard-view"];

  function showView(id) {
    views.forEach(view => byId(view).classList.toggle("hidden", view !== id));
    window.scrollTo({ top: 0, behavior: "smooth" });
  }

  function escapeHtml(value) {
    return String(value ?? "")
      .replaceAll("&", "&amp;")
      .replaceAll("<", "&lt;")
      .replaceAll(">", "&gt;")
      .replaceAll('"', "&quot;")
      .replaceAll("'", "&#039;");
  }

  const formatPercent = value => `${Math.round(Number(value || 0) * 100)}%`;

  function formatDate(value) {
    const date = new Date(value);
    return Number.isNaN(date.valueOf())
      ? "Unknown date"
      : new Intl.DateTimeFormat(undefined, { dateStyle: "medium", timeStyle: "short" }).format(date);
  }

  function normalizeError(payload, status) {
    if (typeof payload === "string") return payload;
    if (payload?.error) {
      const details = Array.isArray(payload.details) ? ` ${payload.details.join(" ")}` : "";
      return `${payload.error}${details}`;
    }
    if (payload?.title) return payload.detail || payload.title;
    return `The assessment service returned HTTP ${status}.`;
  }

  function validGitHubUrl(value) {
    try {
      const url = new URL(value);
      const parts = url.pathname.replace(/\/+$/, "").split("/").filter(Boolean);
      return url.protocol === "https:" &&
        url.hostname.toLowerCase() === "github.com" &&
        parts.length === 2;
    } catch {
      return false;
    }
  }

  async function assess(event) {
    event.preventDefault();
    const repoUrl = byId("repo-url").value.trim();
    const target = new FormData(event.currentTarget).get("target");
    const error = byId("url-error");

    if (!validGitHubUrl(repoUrl)) {
      error.textContent = "Enter a public repository URL such as https://github.com/owner/repository.";
      byId("repo-url").focus();
      return;
    }

    error.textContent = "";
    state.target = target;
    showView("loading-view");
    startLoadingMessages();

    try {
      const response = await fetch("/assess", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ repoUrl, target })
      });
      const contentType = response.headers.get("content-type") || "";
      const payload = contentType.includes("json") ? await response.json() : await response.text();
      if (!response.ok) throw new Error(normalizeError(payload, response.status));

      state.assessment = payload;
      renderDashboard();
      showView("dashboard-view");
    } catch (errorValue) {
      byId("error-message").textContent =
        errorValue instanceof Error ? errorValue.message : "An unexpected error occurred.";
      showView("error-view");
    } finally {
      stopLoadingMessages();
    }
  }

  function startLoadingMessages() {
    const messages = [
      "Creating a versioned analysis workspace...",
      "Detecting languages, frameworks, and build systems...",
      "Resolving dependencies and inspecting native binaries...",
      "Scanning for architecture-specific code patterns...",
      "Validating evidence and preparing the report..."
    ];
    let index = 0;
    byId("loading-message").textContent = messages[index];
    state.loadingTimer = window.setInterval(() => {
      index = Math.min(index + 1, messages.length - 1);
      byId("loading-message").textContent = messages[index];
      document.querySelectorAll(".scan-steps span").forEach((step, stepIndex) => {
        step.classList.toggle("active", stepIndex <= Math.min(index, 3));
      });
    }, 3200);
  }

  function stopLoadingMessages() {
    if (state.loadingTimer) window.clearInterval(state.loadingTimer);
    state.loadingTimer = null;
  }

  const dependencyBlockers = doc => (doc.dependencies || [])
    .filter(d => ["blocked", "emulation-only", "unknown"].includes(d.architectureStatus));
  const codeBlockers = doc => (doc.codeFindings || [])
    .filter(f => ["critical", "high"].includes(f.severity));

  function renderDashboard() {
    const doc = state.assessment;
    const deps = doc.dependencies || [];
    const findings = doc.codeFindings || [];
    const blockers = dependencyBlockers(doc);
    const highCode = codeBlockers(doc);
    const ready = deps.filter(d => d.architectureStatus === "ready").length;
    const dependencyResolution = deps.length
      ? formatPercent(doc.scanCoverage.dependencyResolutionRate)
      : "N/A";

    byId("repo-owner").textContent = new URL(doc.repository.url).pathname.split("/")[1] || "Repository";
    byId("repo-name").textContent = doc.repository.name;
    byId("repo-branch").textContent = doc.repository.defaultBranch;
    byId("repo-commit").textContent = doc.repository.commitSha.slice(0, 8);
    byId("repo-generated").textContent = formatDate(doc.generatedAt);
    byId("target-chip").textContent = state.target === "Arm64EC" ? "Arm64EC target" : "ARM64 native target";

    byId("blocker-count").textContent = blockers.length + highCode.length;
    byId("ready-count").textContent = ready;
    byId("ready-caption").textContent = `${ready} of ${deps.length} dependencies`;
    byId("finding-count").textContent = findings.length;
    byId("finding-caption").textContent = `${highCode.length} high-severity`;
    byId("resolution-rate").textContent = dependencyResolution;
    byId("resolution-caption").textContent = deps.length
      ? `${doc.scanCoverage.scannersCompleted.length} scanners completed`
      : "No declared dependencies";
    byId("dependency-tab-count").textContent = deps.length;
    byId("code-tab-count").textContent = findings.length;

    renderTopBlockers(blockers, highCode);
    renderTechnology(doc.technology);
    renderBuildChecks(doc.buildFindings);
    renderCoverage(doc.scanCoverage);
    renderUnknowns(doc.unknowns || []);
    renderDependencies();
    renderSeverityFilters();
    renderCodeFindings();
    renderReport();
    activatePanel("overview-panel");
  }

  function renderTopBlockers(dependencies, findings) {
    const items = [
      ...dependencies.map(d => ({
        title: d.name,
        detail: `${d.ecosystem} dependency · ${d.evidence?.[0]?.observation || "Compatibility needs verification."}`,
        status: d.architectureStatus
      })),
      ...findings.map(f => ({
        title: f.description,
        detail: `${f.file}${f.line ? `:${f.line}` : ""} · ${f.ruleId}`,
        status: f.severity
      }))
    ].slice(0, 6);

    byId("top-blockers").innerHTML = items.length
      ? items.map(item => `
          <div class="blocker-item">
            <span class="blocker-dot ${escapeHtml(item.status)}"></span>
            <div><strong>${escapeHtml(item.title)}</strong><small>${escapeHtml(item.detail)}</small></div>
            <span class="badge ${escapeHtml(item.status)}">${escapeHtml(item.status)}</span>
          </div>`).join("")
      : `<div class="empty-compact">No immediate blockers were detected. Review unknowns and validate on ARM64 hardware.</div>`;
  }

  function renderTechnology(technology) {
    const groups = [
      ["Languages", technology.languages],
      ["Frameworks", technology.frameworks],
      ["Build systems", technology.buildSystems],
      ["Package managers", technology.packageManagers],
      ["Installers", technology.installers],
      ["CI systems", technology.ciSystems]
    ];
    byId("technology-summary").innerHTML = groups
      .filter(([, values]) => values?.length)
      .map(([label, values]) => `
        <div class="technology-group">
          <strong>${label}</strong>
          <div class="tag-list">${values.map(v => `<span class="tag">${escapeHtml(v)}</span>`).join("")}</div>
        </div>`).join("") ||
      `<p class="muted">No technology signals were detected.</p>`;
  }

  function renderBuildChecks(build) {
    const checks = [
      ["Explicit ARM64 target", build.arm64TargetExists],
      ["Explicit Arm64EC target", build.arm64EcTargetExists],
      ["Explicit ARM64 CI job", build.arm64CiJobExists],
      ["Explicit ARM64 packaging", build.packagingSupportsArm64],
      ["Tests detected", build.testsExist]
    ];
    byId("build-checks").innerHTML = checks.map(([label, value]) => `
      <div class="check-item">
        <span class="check-icon ${value ? "yes" : ""}">${value ? "✓" : "–"}</span>
        <span>${escapeHtml(label)}<small>${value ? "Detected" : "Not found"}</small></span>
      </div>`).join("");
  }

  function renderCoverage(coverage) {
    const dependencyCount = state.assessment?.dependencies?.length || 0;
    const dependencyResolution = dependencyCount
      ? formatPercent(coverage.dependencyResolutionRate)
      : "N/A";
    const dependencyProgress = dependencyCount
      ? Math.round(coverage.dependencyResolutionRate * 100)
      : 0;
    const total = Math.max(coverage.filesTotal || 0, 1);
    const filesRate = Math.min(1, (coverage.filesScanned || 0) / total);
    const failed = coverage.scannersFailed || [];
    byId("coverage-label").textContent = failed.length ? "Provisional" : "Complete";
    byId("coverage-label").className = `status-pill badge ${failed.length ? "unknown" : "ready"}`;
    byId("coverage-details").innerHTML = `
      <div>
        <div class="progress-label"><span>Files scanned</span><strong>${coverage.filesScanned} / ${coverage.filesTotal}</strong></div>
        <div class="progress-track"><div class="progress-bar" style="width:${Math.round(filesRate * 100)}%"></div></div>
        <div class="progress-label" style="margin-top:16px"><span>Dependencies resolved</span><strong>${dependencyResolution}</strong></div>
        <div class="progress-track"><div class="progress-bar" style="width:${dependencyProgress}%"></div></div>
      </div>
      <div class="scanner-list">
        ${(coverage.scannersCompleted || []).map(s => `<span class="scanner">✓ ${escapeHtml(s)}</span>`).join("")}
        ${failed.map(s => `<span class="scanner failed">! ${escapeHtml(s)}</span>`).join("")}
      </div>`;
  }

  function renderUnknowns(unknowns) {
    byId("unknowns-card").classList.toggle("hidden", unknowns.length === 0);
    byId("unknowns-list").innerHTML = unknowns.map(u => `
      <div class="unknown-item">
        ${escapeHtml(u.description)}
        <span>${escapeHtml(u.area)}${u.requiredSkill ? ` · Needs ${escapeHtml(u.requiredSkill)}` : ""}</span>
      </div>`).join("");
  }

  function renderDependencies() {
    const search = state.dependencyFilter.search.toLowerCase();
    const status = state.dependencyFilter.status;
    const rows = (state.assessment.dependencies || []).filter(d => {
      const matchesText = !search ||
        `${d.name} ${d.ecosystem} ${d.type} ${d.version || ""}`.toLowerCase().includes(search);
      return matchesText && (status === "all" || d.architectureStatus === status);
    });

    byId("dependency-rows").innerHTML = rows.map(d => {
      const evidence = d.evidence?.[0];
      const source = evidence?.path || evidence?.artifact || "registry";
      return `<tr>
        <td><strong>${escapeHtml(d.name)}</strong><small>${escapeHtml(d.version || "Version unresolved")}</small></td>
        <td>${escapeHtml(d.ecosystem)}</td>
        <td>${escapeHtml(d.type)}</td>
        <td><span class="badge ${escapeHtml(d.architectureStatus)}">${escapeHtml(d.architectureStatus)}</span></td>
        <td>${(d.availableArchitectures || []).map(a => `<span class="tag">${escapeHtml(a)}</span>`).join(" ") || "—"}</td>
        <td class="confidence">${formatPercent(d.confidence)}</td>
        <td class="evidence-cell"><strong>${escapeHtml(source)}</strong><small>${escapeHtml(evidence?.observation || "No observation")}</small></td>
      </tr>`;
    }).join("");
    byId("dependency-empty").classList.toggle("hidden", rows.length > 0);
  }

  function renderSeverityFilters() {
    const findings = state.assessment.codeFindings || [];
    const order = ["all", "critical", "high", "medium", "low", "informational"];
    const counts = Object.fromEntries(order.map(s => [
      s,
      s === "all" ? findings.length : findings.filter(f => f.severity === s).length
    ]));
    byId("severity-filters").innerHTML = order
      .filter(s => s === "all" || counts[s] > 0)
      .map(s => `<button class="filter-pill ${state.severity === s ? "active" : ""}" data-severity="${s}" type="button">${s} · ${counts[s]}</button>`)
      .join("");
  }

  function renderCodeFindings() {
    const findings = (state.assessment.codeFindings || [])
      .filter(f => state.severity === "all" || f.severity === state.severity);
    byId("code-findings").innerHTML = findings.map(f => {
      const evidence = f.evidence?.[0];
      return `<article class="finding-card" data-severity="${escapeHtml(f.severity)}">
        <span class="finding-accent"></span>
        <div class="finding-body">
          <div class="finding-top">
            <div><p class="eyebrow">${escapeHtml(f.ruleId)} · ${escapeHtml(f.category)}</p><h3 class="finding-title">${escapeHtml(f.description)}</h3></div>
            <span class="badge ${escapeHtml(f.severity)}">${escapeHtml(f.severity)}</span>
          </div>
          <div class="finding-evidence">
            <div class="finding-path">${escapeHtml(f.file)}${f.line ? `:${f.line}` : ""}</div>
            ${escapeHtml(evidence?.observation || "File-level finding")}
          </div>
        </div>
      </article>`;
    }).join("");
    byId("code-empty").classList.toggle("hidden", findings.length > 0);
  }

  function renderReport() {
    const doc = state.assessment;
    const deps = doc.dependencies || [];
    const blockers = dependencyBlockers(doc);
    const highCode = codeBlockers(doc);
    const ready = deps.filter(d => d.architectureStatus === "ready").length;
    const dependencyResolution = deps.length
      ? formatPercent(doc.scanCoverage.dependencyResolutionRate)
      : "N/A";

    byId("report-title").textContent = doc.repository.name;
    byId("report-subtitle").textContent =
      `Windows on Arm repository assessment for ${state.target === "Arm64EC" ? "Arm64EC" : "ARM64 native"}.`;
    byId("report-meta").innerHTML = [
      ["Repository", doc.repository.url],
      ["Commit", doc.repository.commitSha.slice(0, 12)],
      ["Branch", doc.repository.defaultBranch],
      ["Generated", formatDate(doc.generatedAt)]
    ].map(([label, value]) => `<div class="report-meta-item"><small>${label}</small><strong>${escapeHtml(value)}</strong></div>`).join("");

    byId("report-summary").innerHTML = [
      [blockers.length + highCode.length, "Potential blockers"],
      [ready, "Ready dependencies"],
      [doc.codeFindings.length, "Code findings"],
      [dependencyResolution, deps.length ? "Dependencies resolved" : "No declared dependencies"]
    ].map(([value, label]) => `<div class="report-stat"><strong>${value}</strong><small>${label}</small></div>`).join("");

    const tech = doc.technology;
    byId("report-technology").innerHTML = reportRows([
      ["Languages", tech.languages],
      ["Frameworks", tech.frameworks],
      ["Build systems", tech.buildSystems],
      ["Package managers", tech.packageManagers],
      ["Installers", tech.installers],
      ["CI systems", tech.ciSystems]
    ]);

    const reportBlockers = [
      ...blockers.map(d => `${d.name} (${d.ecosystem}): ${d.architectureStatus} — ${d.evidence?.[0]?.observation || "Needs verification."}`),
      ...highCode.map(f => `${f.file}${f.line ? `:${f.line}` : ""}: ${f.description}`)
    ];
    byId("report-blockers").innerHTML = listOrEmpty(reportBlockers, "No immediate blockers were detected.");

    const build = doc.buildFindings;
    const windows = doc.windowsExperience;
    byId("report-build").innerHTML = reportRows([
      ["Explicit ARM64 target", [build.arm64TargetExists ? "Detected" : "Not found; portable builds may not require one"]],
      ["Explicit Arm64EC target", [build.arm64EcTargetExists ? "Detected" : "Not found"]],
      ["Explicit ARM64 CI", [build.arm64CiJobExists ? "Detected" : "Not found"]],
      ["Explicit packaging", [build.packagingSupportsArm64 ? "ARM64 signal detected" : "No ARM64-specific signal found"]],
      ["Windows UI", [windows.uiTechnology]],
      ["Installer", [windows.installerExists ? "Detected" : "Not detected"]]
    ]);
    byId("report-unknowns").innerHTML = listOrEmpty(
      (doc.unknowns || []).map(u => u.description),
      "No unresolved questions were reported."
    );
  }

  function reportRows(groups) {
    return `<table class="report-table"><tbody>${groups.map(([label, values]) => `
      <tr><th>${escapeHtml(label)}</th><td>${values?.length ? values.map(escapeHtml).join(", ") : "None detected"}</td></tr>`
    ).join("")}</tbody></table>`;
  }

  function listOrEmpty(items, emptyText) {
    return items.length
      ? `<ul class="report-list">${items.map(i => `<li>${escapeHtml(i)}</li>`).join("")}</ul>`
      : `<p class="muted">${escapeHtml(emptyText)}</p>`;
  }

  function activatePanel(panelId) {
    document.querySelectorAll(".tab-panel").forEach(panel =>
      panel.classList.toggle("hidden", panel.id !== panelId));
    document.querySelectorAll(".tab").forEach(tab => {
      const active = tab.dataset.panel === panelId;
      tab.classList.toggle("active", active);
      tab.setAttribute("aria-selected", String(active));
    });
  }

  function reset() {
    state.assessment = null;
    state.dependencyFilter = { search: "", status: "all" };
    state.severity = "all";
    byId("dependency-search").value = "";
    byId("dependency-status").value = "all";
    showView("connect-view");
    byId("repo-url").focus();
  }

  function downloadJson() {
    const blob = new Blob([JSON.stringify(state.assessment, null, 2)], { type: "application/json" });
    const link = document.createElement("a");
    link.href = URL.createObjectURL(blob);
    link.download = `${state.assessment.repository.name}-arm-assessment.json`;
    document.body.appendChild(link);
    link.click();
    URL.revokeObjectURL(link.href);
    link.remove();
  }

  byId("assessment-form").addEventListener("submit", assess);
  byId("try-again-button").addEventListener("click", reset);
  byId("back-button").addEventListener("click", reset);
  byId("download-button").addEventListener("click", downloadJson);
  byId("print-button").addEventListener("click", () => {
    activatePanel("report-panel");
    window.setTimeout(() => window.print(), 50);
  });
  byId("dependency-search").addEventListener("input", event => {
    state.dependencyFilter.search = event.target.value;
    renderDependencies();
  });
  byId("dependency-status").addEventListener("change", event => {
    state.dependencyFilter.status = event.target.value;
    renderDependencies();
  });
  document.addEventListener("click", event => {
    const panelButton = event.target.closest("[data-panel]");
    if (panelButton) activatePanel(panelButton.dataset.panel);
    const severityButton = event.target.closest("[data-severity]");
    if (severityButton) {
      state.severity = severityButton.dataset.severity;
      renderSeverityFilters();
      renderCodeFindings();
    }
  });
})();
