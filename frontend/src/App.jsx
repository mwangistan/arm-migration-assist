import { useEffect, useMemo, useState } from "react";
import { assessRepository } from "./api.js";

const loadingMessages = [
  "Creating a versioned analysis workspace...",
  "Detecting languages, frameworks, and build systems...",
  "Resolving dependencies and inspecting native binaries...",
  "Scanning for architecture-specific code patterns...",
  "Validating evidence and preparing the report..."
];

const panels = [
  ["overview", "Overview"],
  ["dependencies", "Dependencies"],
  ["code", "Code findings"],
  ["report", "Report"]
];

function formatPercent(value) {
  return `${Math.round(Number(value || 0) * 100)}%`;
}

function formatDate(value) {
  const date = new Date(value);
  return Number.isNaN(date.valueOf())
    ? "Unknown date"
    : new Intl.DateTimeFormat(undefined, {
        dateStyle: "medium",
        timeStyle: "short"
      }).format(date);
}

function isValidGitHubUrl(value) {
  try {
    const url = new URL(value);
    const parts = url.pathname.replace(/\/+$/, "").split("/").filter(Boolean);
    return (
      url.protocol === "https:" &&
      url.hostname.toLowerCase() === "github.com" &&
      parts.length === 2
    );
  } catch {
    return false;
  }
}

function dependencyBlockers(assessment) {
  return (assessment.dependencies || []).filter((dependency) =>
    ["blocked", "emulation-only", "unknown"].includes(
      dependency.architectureStatus
    )
  );
}

function codeBlockers(assessment) {
  return (assessment.codeFindings || []).filter((finding) =>
    ["critical", "high"].includes(finding.severity)
  );
}

export default function App() {
  const [view, setView] = useState("connect");
  const [assessment, setAssessment] = useState(null);
  const [target, setTarget] = useState("Arm64Native");
  const [error, setError] = useState("");
  const [panel, setPanel] = useState("overview");

  async function handleAssess(repoUrl, selectedTarget) {
    setTarget(selectedTarget);
    setError("");
    setView("loading");

    try {
      const result = await assessRepository(repoUrl, selectedTarget);
      setAssessment(result);
      setPanel("overview");
      setView("dashboard");
    } catch (errorValue) {
      setError(
        errorValue instanceof Error
          ? errorValue.message
          : "An unexpected error occurred."
      );
      setView("error");
    }
  }

  function reset() {
    setAssessment(null);
    setError("");
    setPanel("overview");
    setView("connect");
    window.scrollTo({ top: 0, behavior: "smooth" });
  }

  return (
    <>
      <a className="skip-link" href="#main">
        Skip to content
      </a>
      <AppHeader />
      <main id="main">
        {view === "connect" && <ConnectView onAssess={handleAssess} />}
        {view === "loading" && <LoadingView />}
        {view === "error" && <ErrorView error={error} onReset={reset} />}
        {view === "dashboard" && assessment && (
          <Dashboard
            assessment={assessment}
            target={target}
            panel={panel}
            onPanelChange={setPanel}
            onReset={reset}
          />
        )}
      </main>
    </>
  );
}

function AppHeader() {
  return (
    <header className="app-header">
      <a className="brand" href="/" aria-label="ARM Migration Assist home">
        <span className="brand-mark" aria-hidden="true">
          <svg viewBox="0 0 36 36">
            <path d="M8 27 18 7l10 20h-6l-4-9-4 9H8Z" />
            <path className="brand-cut" d="M15.8 24h4.4L18 19.2 15.8 24Z" />
          </svg>
        </span>
        <span>
          <strong>ARM Migration Assist</strong>
          <small>Repository Assessment Engine</small>
        </span>
      </a>
      <span className="feature-badge">Feature 1</span>
    </header>
  );
}

function ConnectView({ onAssess }) {
  const [repoUrl, setRepoUrl] = useState("https://github.com/nothings/stb");
  const [target, setTarget] = useState("Arm64Native");
  const [urlError, setUrlError] = useState("");

  function submit(event) {
    event.preventDefault();
    const normalizedUrl = repoUrl.trim();
    if (!isValidGitHubUrl(normalizedUrl)) {
      setUrlError(
        "Enter a public repository URL such as https://github.com/owner/repository."
      );
      return;
    }

    setUrlError("");
    onAssess(normalizedUrl, target);
  }

  return (
    <section className="connect-view">
      <div className="hero-copy">
        <p className="eyebrow">Windows on Arm readiness</p>
        <h1>Know what stands between your app and ARM64.</h1>
        <p className="hero-lede">
          Connect a public GitHub repository. Deterministic scanners inspect its
          technology, dependencies, build configuration, native binaries, and
          architecture-specific code.
        </p>
        <div className="trust-row" aria-label="Assessment qualities">
          <span><i className="trust-dot" /> Evidence-linked</span>
          <span><i className="trust-dot" /> Deterministic</span>
          <span><i className="trust-dot" /> Read-only</span>
        </div>
      </div>

      <section className="connect-card" aria-labelledby="connect-title">
        <div className="step-label">01 / Connect</div>
        <h2 id="connect-title">Assess a repository</h2>
        <p>
          Enter a public GitHub URL and choose the architecture you are
          investigating.
        </p>
        <form onSubmit={submit} noValidate>
          <label htmlFor="repo-url">GitHub repository URL</label>
          <div className="url-field">
            <GitHubIcon />
            <input
              id="repo-url"
              name="repoUrl"
              type="url"
              inputMode="url"
              autoComplete="url"
              placeholder="https://github.com/owner/repository"
              value={repoUrl}
              onChange={(event) => setRepoUrl(event.target.value)}
              aria-describedby="url-error"
              required
            />
          </div>
          <p id="url-error" className="field-error" role="alert">
            {urlError}
          </p>

          <fieldset>
            <legend>Assessment target</legend>
            <div className="target-options">
              <TargetOption
                value="Arm64Native"
                selected={target}
                onSelect={setTarget}
                title="ARM64 native"
                description="Recompile the application and native dependencies for ARM64."
              />
              <TargetOption
                value="Arm64EC"
                selected={target}
                onSelect={setTarget}
                title="Arm64EC"
                description="Mix ARM64EC code with existing x64 dependencies during migration."
              />
            </div>
          </fieldset>

          <div className="target-note">
            <strong>Not sure which to choose?</strong> Start with ARM64 native
            for the strictest assessment. Feature 2 will use these facts to
            recommend a final strategy.
          </div>
          <button className="primary-button" type="submit">
            <span>Analyze repository</span><span aria-hidden="true">→</span>
          </button>
        </form>
      </section>

      <div className="process-strip" aria-label="Assessment stages">
        {[
          ["01", "Clone & snapshot"],
          ["02", "Discover stack"],
          ["03", "Inspect dependencies"],
          ["04", "Find blockers"]
        ].map(([number, label]) => (
          <div key={number}><b>{number}</b><span>{label}</span></div>
        ))}
      </div>
    </section>
  );
}

function TargetOption({ value, selected, onSelect, title, description }) {
  return (
    <label className="target-option">
      <input
        type="radio"
        name="target"
        value={value}
        checked={selected === value}
        onChange={() => onSelect(value)}
      />
      <span><strong>{title}</strong><small>{description}</small></span>
    </label>
  );
}

function GitHubIcon() {
  return (
    <svg aria-hidden="true" viewBox="0 0 24 24">
      <path d="M12 .7a11.5 11.5 0 0 0-3.64 22.4c.58.1.79-.25.79-.56v-2.2c-3.22.7-3.9-1.37-3.9-1.37-.52-1.34-1.28-1.7-1.28-1.7-1.05-.72.08-.7.08-.7 1.16.08 1.77 1.19 1.77 1.19 1.03 1.77 2.7 1.26 3.36.96.1-.75.4-1.26.73-1.55-2.57-.29-5.27-1.28-5.27-5.69 0-1.26.45-2.28 1.19-3.09-.12-.29-.52-1.46.11-3.05 0 0 .97-.31 3.16 1.18a10.95 10.95 0 0 1 5.76 0c2.2-1.49 3.16-1.18 3.16-1.18.63 1.59.23 2.76.11 3.05.74.81 1.19 1.83 1.19 3.09 0 4.42-2.71 5.39-5.29 5.68.42.36.79 1.07.79 2.16v3.2c0 .31.21.67.8.56A11.5 11.5 0 0 0 12 .7Z" />
    </svg>
  );
}

function LoadingView() {
  const [messageIndex, setMessageIndex] = useState(0);

  useEffect(() => {
    const timer = window.setInterval(() => {
      setMessageIndex((current) =>
        Math.min(current + 1, loadingMessages.length - 1)
      );
    }, 3200);
    return () => window.clearInterval(timer);
  }, []);

  return (
    <section className="loading-view" aria-live="polite">
      <div className="scan-visual" aria-hidden="true">
        <span className="scan-orbit" /><span className="scan-core">ARM</span>
      </div>
      <p className="eyebrow">Assessment in progress</p>
      <h1>Inspecting the repository</h1>
      <p>{loadingMessages[messageIndex]}</p>
      <div className="scan-steps" aria-hidden="true">
        {["Repository", "Technology", "Dependencies", "Code"].map(
          (step, index) => (
            <span className={index <= Math.min(messageIndex, 3) ? "active" : ""} key={step}>
              {step}
            </span>
          )
        )}
      </div>
      <p className="loading-detail">
        Large repositories can take several minutes. Keep this page open.
      </p>
    </section>
  );
}

function ErrorView({ error, onReset }) {
  return (
    <section className="message-view" role="alert">
      <span className="message-icon">!</span>
      <p className="eyebrow">Assessment stopped</p>
      <h1>We could not analyze this repository.</h1>
      <p>{error}</p>
      <button className="secondary-button" type="button" onClick={onReset}>
        Try another repository
      </button>
    </section>
  );
}

function Dashboard({
  assessment,
  target,
  panel,
  onPanelChange,
  onReset
}) {
  function downloadJson() {
    const blob = new Blob([JSON.stringify(assessment, null, 2)], {
      type: "application/json"
    });
    const link = document.createElement("a");
    link.href = URL.createObjectURL(blob);
    link.download = `${assessment.repository.name}-arm-assessment.json`;
    document.body.appendChild(link);
    link.click();
    URL.revokeObjectURL(link.href);
    link.remove();
  }

  function printReport() {
    onPanelChange("report");
    window.setTimeout(() => window.print(), 50);
  }

  const owner =
    new URL(assessment.repository.url).pathname.split("/")[1] || "Repository";
  const tabCounts = {
    dependencies: assessment.dependencies?.length || 0,
    code: assessment.codeFindings?.length || 0
  };

  return (
    <section className="dashboard">
      <header className="result-header">
        <div>
          <button className="text-button" type="button" onClick={onReset}>
            ← New assessment
          </button>
          <div className="repo-heading">
            <span className="repo-icon" aria-hidden="true">GH</span>
            <div>
              <p className="eyebrow">{owner}</p>
              <h1>{assessment.repository.name}</h1>
              <p className="repo-meta">
                <span>{assessment.repository.defaultBranch}</span>
                <span>{assessment.repository.commitSha.slice(0, 8)}</span>
                <span>{formatDate(assessment.generatedAt)}</span>
              </p>
            </div>
          </div>
        </div>
        <div className="result-actions">
          <span className="target-chip">
            {target === "Arm64EC" ? "Arm64EC target" : "ARM64 native target"}
          </span>
          <button className="secondary-button" type="button" onClick={downloadJson}>
            Download JSON
          </button>
          <button className="primary-button compact" type="button" onClick={printReport}>
            Print report
          </button>
        </div>
      </header>

      <nav className="tabs" aria-label="Assessment sections">
        {panels.map(([id, label]) => (
          <button
            className={`tab ${panel === id ? "active" : ""}`}
            type="button"
            aria-selected={panel === id}
            onClick={() => onPanelChange(id)}
            key={id}
          >
            {label}
            {tabCounts[id] !== undefined && <span>{tabCounts[id]}</span>}
          </button>
        ))}
      </nav>

      {panel === "overview" && (
        <Overview assessment={assessment} onPanelChange={onPanelChange} />
      )}
      {panel === "dependencies" && <Dependencies assessment={assessment} />}
      {panel === "code" && <CodeFindings assessment={assessment} />}
      {panel === "report" && <Report assessment={assessment} target={target} />}
    </section>
  );
}

function Overview({ assessment, onPanelChange }) {
  const dependencies = assessment.dependencies || [];
  const findings = assessment.codeFindings || [];
  const blockers = dependencyBlockers(assessment);
  const highCode = codeBlockers(assessment);
  const ready = dependencies.filter(
    (dependency) => dependency.architectureStatus === "ready"
  ).length;
  const resolution = dependencies.length
    ? formatPercent(assessment.scanCoverage.dependencyResolutionRate)
    : "N/A";

  return (
    <div id="overview-panel" className="tab-panel">
      <div className="boundary-note">
        <strong>Feature 1 assessment</strong> This dashboard reports measured
        repository facts. Overall readiness scoring and migration strategy
        recommendations are produced by Feature 2.
      </div>
      <div className="metric-grid">
        <Metric
          emphasis
          label="Potential blockers"
          value={blockers.length + highCode.length}
          caption="Dependencies and high-severity code findings"
        />
        <Metric
          label="ARM64-ready dependencies"
          value={ready}
          caption={`${ready} of ${dependencies.length} dependencies`}
        />
        <Metric
          label="Code findings"
          value={findings.length}
          caption={`${highCode.length} high-severity`}
        />
        <Metric
          label="Dependency resolution"
          value={resolution}
          caption={
            dependencies.length
              ? `${assessment.scanCoverage.scannersCompleted.length} scanners completed`
              : "No declared dependencies"
          }
        />
      </div>

      <div className="overview-grid">
        <article className="panel span-2">
          <PanelHeading eyebrow="Prioritize" title="Potential blockers">
            <button
              className="text-button tab-link"
              type="button"
              onClick={() => onPanelChange("dependencies")}
            >
              View all findings →
            </button>
          </PanelHeading>
          <BlockerList dependencies={blockers} findings={highCode} />
        </article>
        <article className="panel">
          <PanelHeading eyebrow="Technology profile" title="Detected stack" />
          <Technology technology={assessment.technology} />
        </article>
        <article className="panel">
          <PanelHeading
            eyebrow="Build signals"
            title="Explicit ARM64 configuration"
          />
          <BuildChecks build={assessment.buildFindings} />
          <p className="signal-note">
            “Not found” means no explicit repository signal was detected.
            Portable source can still be ARM64-compatible without a dedicated
            target.
          </p>
        </article>
        <Coverage coverage={assessment.scanCoverage} dependencyCount={dependencies.length} />
        {(assessment.unknowns || []).length > 0 && (
          <article className="panel span-2">
            <PanelHeading eyebrow="Needs verification" title="Open questions" />
            <div className="unknowns-list">
              {assessment.unknowns.map((unknown, index) => (
                <div className="unknown-item" key={`${unknown.area}-${index}`}>
                  {unknown.description}
                  <span>
                    {unknown.area}
                    {unknown.requiredSkill
                      ? ` · Needs ${unknown.requiredSkill}`
                      : ""}
                  </span>
                </div>
              ))}
            </div>
          </article>
        )}
      </div>
    </div>
  );
}

function Metric({ emphasis = false, label, value, caption }) {
  return (
    <article className={`metric-card ${emphasis ? "emphasis" : ""}`}>
      <span className="metric-label">{label}</span>
      <strong>{value}</strong>
      <small>{caption}</small>
    </article>
  );
}

function PanelHeading({ eyebrow, title, children }) {
  return (
    <div className="panel-heading">
      <div><p className="eyebrow">{eyebrow}</p><h2>{title}</h2></div>
      {children}
    </div>
  );
}

function BlockerList({ dependencies, findings }) {
  const items = [
    ...dependencies.map((dependency) => ({
      title: dependency.name,
      detail: `${dependency.ecosystem} dependency · ${
        dependency.evidence?.[0]?.observation ||
        "Compatibility needs verification."
      }`,
      status: dependency.architectureStatus
    })),
    ...findings.map((finding) => ({
      title: finding.description,
      detail: `${finding.file}${finding.line ? `:${finding.line}` : ""} · ${
        finding.ruleId
      }`,
      status: finding.severity
    }))
  ].slice(0, 6);

  if (items.length === 0) {
    return (
      <div className="empty-compact">
        No immediate blockers were detected. Review unknowns and validate on
        ARM64 hardware.
      </div>
    );
  }

  return (
    <div className="blocker-list">
      {items.map((item, index) => (
        <div className="blocker-item" key={`${item.title}-${index}`}>
          <span className={`blocker-dot ${item.status}`} />
          <div><strong>{item.title}</strong><small>{item.detail}</small></div>
          <span className={`badge ${item.status}`}>{item.status}</span>
        </div>
      ))}
    </div>
  );
}

function Technology({ technology }) {
  const groups = [
    ["Languages", technology.languages],
    ["Frameworks", technology.frameworks],
    ["Build systems", technology.buildSystems],
    ["Package managers", technology.packageManagers],
    ["Installers", technology.installers],
    ["CI systems", technology.ciSystems]
  ].filter(([, values]) => values?.length);

  if (groups.length === 0) {
    return <p className="muted">No technology signals were detected.</p>;
  }

  return (
    <div className="technology-groups">
      {groups.map(([label, values]) => (
        <div className="technology-group" key={label}>
          <strong>{label}</strong>
          <div className="tag-list">
            {values.map((value) => <span className="tag" key={value}>{value}</span>)}
          </div>
        </div>
      ))}
    </div>
  );
}

function BuildChecks({ build }) {
  const checks = [
    ["Explicit ARM64 target", build.arm64TargetExists],
    ["Explicit Arm64EC target", build.arm64EcTargetExists],
    ["Explicit ARM64 CI job", build.arm64CiJobExists],
    ["Explicit ARM64 packaging", build.packagingSupportsArm64],
    ["Tests detected", build.testsExist]
  ];

  return (
    <div className="check-list">
      {checks.map(([label, value]) => (
        <div className="check-item" key={label}>
          <span className={`check-icon ${value ? "yes" : ""}`}>
            {value ? "✓" : "–"}
          </span>
          <span>{label}<small>{value ? "Detected" : "Not found"}</small></span>
        </div>
      ))}
    </div>
  );
}

function Coverage({ coverage, dependencyCount }) {
  const failed = coverage.scannersFailed || [];
  const total = Math.max(coverage.filesTotal || 0, 1);
  const fileProgress = Math.min(100, Math.round(((coverage.filesScanned || 0) / total) * 100));
  const dependencyProgress = dependencyCount
    ? Math.round(coverage.dependencyResolutionRate * 100)
    : 0;

  return (
    <article className="panel span-2">
      <PanelHeading eyebrow="Coverage" title="Scanner confidence">
        <span className={`status-pill badge ${failed.length ? "unknown" : "ready"}`}>
          {failed.length ? "Provisional" : "Complete"}
        </span>
      </PanelHeading>
      <div className="coverage-layout">
        <div>
          <Progress
            label="Files scanned"
            value={`${coverage.filesScanned} / ${coverage.filesTotal}`}
            percent={fileProgress}
          />
          <Progress
            label="Dependencies resolved"
            value={
              dependencyCount
                ? formatPercent(coverage.dependencyResolutionRate)
                : "N/A"
            }
            percent={dependencyProgress}
            spaced
          />
        </div>
        <div className="scanner-list">
          {(coverage.scannersCompleted || []).map((scanner) => (
            <span className="scanner" key={scanner}>✓ {scanner}</span>
          ))}
          {failed.map((scanner) => (
            <span className="scanner failed" key={scanner}>! {scanner}</span>
          ))}
        </div>
      </div>
    </article>
  );
}

function Progress({ label, value, percent, spaced = false }) {
  return (
    <div style={spaced ? { marginTop: 16 } : undefined}>
      <div className="progress-label"><span>{label}</span><strong>{value}</strong></div>
      <div className="progress-track">
        <div className="progress-bar" style={{ width: `${percent}%` }} />
      </div>
    </div>
  );
}

function Dependencies({ assessment }) {
  const [search, setSearch] = useState("");
  const [status, setStatus] = useState("all");
  const rows = useMemo(() => {
    const normalizedSearch = search.toLowerCase();
    return (assessment.dependencies || []).filter((dependency) => {
      const matchesText =
        !normalizedSearch ||
        `${dependency.name} ${dependency.ecosystem} ${dependency.type} ${
          dependency.version || ""
        }`
          .toLowerCase()
          .includes(normalizedSearch);
      return (
        matchesText &&
        (status === "all" || dependency.architectureStatus === status)
      );
    });
  }, [assessment, search, status]);

  return (
    <div id="dependencies-panel" className="tab-panel">
      <div className="section-heading">
        <div>
          <p className="eyebrow">Dependency matrix</p>
          <h2>Architecture compatibility</h2>
          <p>Registry and binary evidence for every resolved component.</p>
        </div>
        <div className="filters">
          <label className="search-control">
            <span className="sr-only">Filter dependencies</span>
            <input
              type="search"
              placeholder="Filter dependencies"
              value={search}
              onChange={(event) => setSearch(event.target.value)}
            />
          </label>
          <label>
            <span className="sr-only">Filter by status</span>
            <select value={status} onChange={(event) => setStatus(event.target.value)}>
              <option value="all">All statuses</option>
              <option value="blocked">Blocked</option>
              <option value="emulation-only">Emulation only</option>
              <option value="unknown">Unknown</option>
              <option value="ready">Ready</option>
            </select>
          </label>
        </div>
      </div>
      <div className="table-wrap">
        <table>
          <thead>
            <tr>
              <th>Dependency</th><th>Ecosystem</th><th>Type</th>
              <th>ARM status</th><th>Architectures</th>
              <th>Confidence</th><th>Evidence</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((dependency, index) => {
              const evidence = dependency.evidence?.[0];
              const source = evidence?.path || evidence?.artifact || "registry";
              return (
                <tr key={`${dependency.ecosystem}-${dependency.name}-${index}`}>
                  <td><strong>{dependency.name}</strong><small>{dependency.version || "Version unresolved"}</small></td>
                  <td>{dependency.ecosystem}</td>
                  <td>{dependency.type}</td>
                  <td><span className={`badge ${dependency.architectureStatus}`}>{dependency.architectureStatus}</span></td>
                  <td>
                    {(dependency.availableArchitectures || []).length
                      ? dependency.availableArchitectures.map((architecture) => (
                          <span className="tag" key={architecture}>{architecture}</span>
                        ))
                      : "—"}
                  </td>
                  <td className="confidence">{formatPercent(dependency.confidence)}</td>
                  <td className="evidence-cell"><strong>{source}</strong><small>{evidence?.observation || "No observation"}</small></td>
                </tr>
              );
            })}
          </tbody>
        </table>
        {rows.length === 0 && (
          <p className="empty-state">No dependencies match these filters.</p>
        )}
      </div>
    </div>
  );
}

function CodeFindings({ assessment }) {
  const [severity, setSeverity] = useState("all");
  const allFindings = assessment.codeFindings || [];
  const order = ["all", "critical", "high", "medium", "low", "informational"];
  const counts = Object.fromEntries(
    order.map((value) => [
      value,
      value === "all"
        ? allFindings.length
        : allFindings.filter((finding) => finding.severity === value).length
    ])
  );
  const findings = allFindings.filter(
    (finding) => severity === "all" || finding.severity === severity
  );

  return (
    <div id="code-panel" className="tab-panel">
      <div className="section-heading">
        <div>
          <p className="eyebrow">Architecture scanner</p>
          <h2>Code compatibility findings</h2>
          <p>Architecture-specific patterns with file and line evidence.</p>
        </div>
        <div className="filter-pills" aria-label="Filter code findings">
          {order
            .filter((value) => value === "all" || counts[value] > 0)
            .map((value) => (
              <button
                className={`filter-pill ${severity === value ? "active" : ""}`}
                type="button"
                onClick={() => setSeverity(value)}
                key={value}
              >
                {value} · {counts[value]}
              </button>
            ))}
        </div>
      </div>
      <div className="finding-list">
        {findings.map((finding, index) => {
          const evidence = finding.evidence?.[0];
          return (
            <article
              className="finding-card"
              data-severity={finding.severity}
              key={`${finding.ruleId}-${finding.file}-${index}`}
            >
              <span className="finding-accent" />
              <div className="finding-body">
                <div className="finding-top">
                  <div>
                    <p className="eyebrow">{finding.ruleId} · {finding.category}</p>
                    <h3 className="finding-title">{finding.description}</h3>
                  </div>
                  <span className={`badge ${finding.severity}`}>{finding.severity}</span>
                </div>
                <div className="finding-evidence">
                  <div className="finding-path">
                    {finding.file}{finding.line ? `:${finding.line}` : ""}
                  </div>
                  {evidence?.observation || "File-level finding"}
                </div>
              </div>
            </article>
          );
        })}
      </div>
      {findings.length === 0 && (
        <p className="empty-state">No code findings match this filter.</p>
      )}
    </div>
  );
}

function Report({ assessment, target }) {
  const dependencies = assessment.dependencies || [];
  const blockers = dependencyBlockers(assessment);
  const highCode = codeBlockers(assessment);
  const ready = dependencies.filter(
    (dependency) => dependency.architectureStatus === "ready"
  ).length;
  const resolution = dependencies.length
    ? formatPercent(assessment.scanCoverage.dependencyResolutionRate)
    : "N/A";
  const technology = assessment.technology;
  const build = assessment.buildFindings;
  const windows = assessment.windowsExperience;
  const blockerDescriptions = [
    ...blockers.map(
      (dependency) =>
        `${dependency.name} (${dependency.ecosystem}): ${
          dependency.architectureStatus
        } — ${dependency.evidence?.[0]?.observation || "Needs verification."}`
    ),
    ...highCode.map(
      (finding) =>
        `${finding.file}${finding.line ? `:${finding.line}` : ""}: ${
          finding.description
        }`
    )
  ];

  return (
    <div id="report-panel" className="tab-panel">
      <article id="report" className="report">
        <div className="report-cover">
          <div>
            <span className="report-mark">ARM</span>
            <p className="eyebrow">Repository assessment report</p>
            <h2>{assessment.repository.name}</h2>
            <p>
              Windows on Arm repository assessment for{" "}
              {target === "Arm64EC" ? "Arm64EC" : "ARM64 native"}.
            </p>
          </div>
          <div className="report-meta-grid">
            {[
              ["Repository", assessment.repository.url],
              ["Commit", assessment.repository.commitSha.slice(0, 12)],
              ["Branch", assessment.repository.defaultBranch],
              ["Generated", formatDate(assessment.generatedAt)]
            ].map(([label, value]) => (
              <div className="report-meta-item" key={label}>
                <small>{label}</small><strong>{value}</strong>
              </div>
            ))}
          </div>
        </div>
        <ReportSection title="Assessment summary">
          <div className="report-summary">
            {[
              [blockers.length + highCode.length, "Potential blockers"],
              [ready, "Ready dependencies"],
              [assessment.codeFindings.length, "Code findings"],
              [resolution, dependencies.length ? "Dependencies resolved" : "No declared dependencies"]
            ].map(([value, label]) => (
              <div className="report-stat" key={label}>
                <strong>{value}</strong><small>{label}</small>
              </div>
            ))}
          </div>
        </ReportSection>
        <ReportSection title="Technology inventory">
          <ReportTable
            rows={[
              ["Languages", technology.languages],
              ["Frameworks", technology.frameworks],
              ["Build systems", technology.buildSystems],
              ["Package managers", technology.packageManagers],
              ["Installers", technology.installers],
              ["CI systems", technology.ciSystems]
            ]}
          />
        </ReportSection>
        <ReportSection title="Potential blockers">
          <ReportList
            items={blockerDescriptions}
            emptyText="No immediate blockers were detected."
          />
        </ReportSection>
        <ReportSection title="Explicit build and Windows signals">
          <ReportTable
            rows={[
              ["Explicit ARM64 target", [build.arm64TargetExists ? "Detected" : "Not found; portable builds may not require one"]],
              ["Explicit Arm64EC target", [build.arm64EcTargetExists ? "Detected" : "Not found"]],
              ["Explicit ARM64 CI", [build.arm64CiJobExists ? "Detected" : "Not found"]],
              ["Explicit packaging", [build.packagingSupportsArm64 ? "ARM64 signal detected" : "No ARM64-specific signal found"]],
              ["Windows UI", [windows.uiTechnology]],
              ["Installer", [windows.installerExists ? "Detected" : "Not detected"]]
            ]}
          />
        </ReportSection>
        <ReportSection title="Unresolved questions">
          <ReportList
            items={(assessment.unknowns || []).map((unknown) => unknown.description)}
            emptyText="No unresolved questions were reported."
          />
        </ReportSection>
        <footer className="report-footer">
          Generated by ARM Migration Assist Feature 1. This report contains
          deterministic assessment facts; migration strategy and planning
          belong to Feature 2.
        </footer>
      </article>
    </div>
  );
}

function ReportSection({ title, children }) {
  return <section className="report-section"><h3>{title}</h3>{children}</section>;
}

function ReportTable({ rows }) {
  return (
    <table className="report-table">
      <tbody>
        {rows.map(([label, values]) => (
          <tr key={label}>
            <th>{label}</th>
            <td>{values?.length ? values.join(", ") : "None detected"}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}

function ReportList({ items, emptyText }) {
  return items.length ? (
    <ul className="report-list">
      {items.map((item, index) => <li key={`${item}-${index}`}>{item}</li>)}
    </ul>
  ) : (
    <p className="muted">{emptyText}</p>
  );
}
