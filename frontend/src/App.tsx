import { useRef, useState, type FormEvent } from 'react';
import {
  Badge,
  Button,
  Field,
  FluentProvider,
  Input,
  MessageBar,
  MessageBarBody,
  ProgressBar,
  Spinner,
  webLightTheme,
} from '@fluentui/react-components';
import {
  ArrowDownload20Regular,
  ArrowRight20Regular,
  Dismiss20Regular,
  Print20Regular,
  Search20Regular,
} from '@fluentui/react-icons';
import {
  AssessmentApiError,
  assessRepository,
  cancelGitHubAuthentication,
  getGitHubAuthentication,
  startGitHubAuthentication,
} from './api';
import type { CodeFinding, DependencyFinding, RepositoryAssessment } from './types';

type BadgeColor =
  | 'brand'
  | 'danger'
  | 'important'
  | 'informative'
  | 'severe'
  | 'subtle'
  | 'success'
  | 'warning';

interface StatusMeta {
  label: string;
  color: BadgeColor;
}

const technologyGroups = [
  ['Languages', 'languages'],
  ['Frameworks', 'frameworks'],
  ['Project types', 'projectTypes'],
  ['Build systems', 'buildSystems'],
  ['Package managers', 'packageManagers'],
  ['Installers', 'installers'],
  ['CI systems', 'ciSystems'],
] as const;

function pluralize(count: number, singular: string, plural = `${singular}s`) {
  return `${count} ${count === 1 ? singular : plural}`;
}

function titleCase(value: string) {
  return value
    .replace(/[-_]/g, ' ')
    .replace(/\b\w/g, (character) => character.toUpperCase());
}

function formatPercent(value: number) {
  return `${Math.round(Math.max(0, Math.min(1, value)) * 100)}%`;
}

function formatDate(value: string) {
  const date = new Date(value);
  return Number.isNaN(date.getTime())
    ? value
    : new Intl.DateTimeFormat(undefined, {
        dateStyle: 'medium',
        timeStyle: 'short',
      }).format(date);
}

function waitForPoll(signal: AbortSignal) {
  return new Promise<void>((resolve, reject) => {
    if (signal.aborted) {
      reject(new DOMException('The operation was aborted.', 'AbortError'));
      return;
    }

    const handleAbort = () => {
      window.clearTimeout(timeout);
      reject(new DOMException('The operation was aborted.', 'AbortError'));
    };
    const timeout = window.setTimeout(() => {
      signal.removeEventListener('abort', handleAbort);
      resolve();
    }, 1_000);
    signal.addEventListener('abort', handleAbort, { once: true });
  });
}

function architectureMeta(status: DependencyFinding['architectureStatus']): StatusMeta {
  switch (status) {
    case 'ready':
      return { label: 'ARM64 ready', color: 'success' };
    case 'emulation-only':
      return { label: 'Emulation only', color: 'warning' };
    case 'blocked':
      return { label: 'Blocked', color: 'danger' };
    default:
      return { label: 'Unknown', color: 'subtle' };
  }
}

function severityMeta(severity: CodeFinding['severity']): StatusMeta {
  switch (severity) {
    case 'critical':
      return { label: 'Critical', color: 'danger' };
    case 'high':
      return { label: 'High', color: 'severe' };
    case 'medium':
      return { label: 'Medium', color: 'warning' };
    case 'low':
      return { label: 'Low', color: 'informative' };
    default:
      return { label: 'Informational', color: 'subtle' };
  }
}

function StatusBadge({ meta }: { meta: StatusMeta }) {
  return (
    <Badge appearance="tint" color={meta.color} size="small">
      {meta.label}
    </Badge>
  );
}

function SignalRow({ label, available }: { label: string; available: boolean }) {
  return (
    <div className="signal-row">
      <span>{label}</span>
      <StatusBadge
        meta={available
          ? { label: 'Detected', color: 'success' }
          : { label: 'Not detected', color: 'subtle' }}
      />
    </div>
  );
}

function EmptyRow({ columns, message }: { columns: number; message: string }) {
  return (
    <tr>
      <td className="empty-table" colSpan={columns}>{message}</td>
    </tr>
  );
}

function AssessmentResults({ assessment }: { assessment: RepositoryAssessment }) {
  const coverage = assessment.scanCoverage.filesTotal === 0
    ? 0
    : assessment.scanCoverage.filesScanned / assessment.scanCoverage.filesTotal;
  const commitSha = assessment.repository.commitSha;

  function downloadAssessment() {
    const blob = new Blob([JSON.stringify(assessment, null, 2)], { type: 'application/json' });
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = `${assessment.repository.name}-assessment.json`;
    anchor.click();
    URL.revokeObjectURL(url);
  }

  function printAssessment() {
    window.print();
  }

  return (
    <div className="results-enter">
      <section className="repository-heading" aria-labelledby="repository-name">
        <div>
          <p className="eyebrow">Assessment result</p>
          <h2 id="repository-name">{assessment.repository.name}</h2>
          <a
            className="repository-link"
            href={assessment.repository.url}
            target="_blank"
            rel="noreferrer"
          >
            {assessment.repository.url}
          </a>
        </div>
        <div className="report-actions">
          <Button
            appearance="secondary"
            icon={<Print20Regular />}
            onClick={printAssessment}
          >
            Print report
          </Button>
          <Button
            appearance="secondary"
            icon={<ArrowDownload20Regular />}
            onClick={downloadAssessment}
          >
            Export JSON
          </Button>
        </div>
      </section>

      <div className="repository-meta" aria-label="Repository metadata">
        <span><strong>Branch</strong> {assessment.repository.defaultBranch}</span>
        <span title={commitSha}><strong>Commit</strong> {commitSha.slice(0, 12)}</span>
        <span><strong>License</strong> {assessment.repository.license ?? 'Not detected'}</span>
        <span><strong>Generated</strong> {formatDate(assessment.generatedAt)}</span>
      </div>

      <div className="metric-grid" aria-label="Assessment summary">
        <div className="metric metric-dependencies">
          <span className="metric-value">{assessment.dependencies.length}</span>
          <span className="metric-label">
            {pluralize(assessment.dependencies.length, 'dependency', 'dependencies')}
          </span>
        </div>
        <div className="metric metric-code">
          <span className="metric-value">{assessment.codeFindings.length}</span>
          <span className="metric-label">
            {pluralize(assessment.codeFindings.length, 'code finding')}
          </span>
        </div>
        <div className="metric metric-files">
          <span className="metric-value">
            {assessment.scanCoverage.filesScanned}/{assessment.scanCoverage.filesTotal}
          </span>
          <span className="metric-label">files scanned</span>
        </div>
        <div className="metric metric-unknowns">
          <span className="metric-value">{assessment.unknowns.length}</span>
          <span className="metric-label">
            {pluralize(assessment.unknowns.length, 'open unknown')}
          </span>
        </div>
      </div>

      <div className="assessment-layout">
        <aside className="section-rail" aria-label="Assessment sections">
          <a href="#coverage">Coverage</a>
          <a href="#technology">Technology</a>
          <a href="#dependencies">Dependencies</a>
          <a href="#code-findings">Code findings</a>
          <a href="#platform-signals">Platform signals</a>
          <a href="#unknowns">Unknowns</a>
        </aside>

        <div className="assessment-content">
          <section className="content-section" id="coverage" aria-labelledby="coverage-heading">
            <div className="section-heading">
              <div>
                <p className="eyebrow">Scan coverage</p>
                <h3 id="coverage-heading">Evidence collection</h3>
              </div>
              <strong>{formatPercent(coverage)}</strong>
            </div>
            <ProgressBar value={coverage} aria-label={`${formatPercent(coverage)} file coverage`} />
            <div className="coverage-details">
              <div>
                <span className="detail-label">Dependency resolution</span>
                <strong>{formatPercent(assessment.scanCoverage.dependencyResolutionRate)}</strong>
              </div>
              <div>
                <span className="detail-label">Completed scanners</span>
                <strong>{assessment.scanCoverage.scannersCompleted.length}</strong>
              </div>
              <div>
                <span className="detail-label">Failed scanners</span>
                <strong>{assessment.scanCoverage.scannersFailed.length}</strong>
              </div>
            </div>
            <div className="scanner-list" aria-label="Scanner status">
              {assessment.scanCoverage.scannersCompleted.map((scanner) => (
                <Badge key={scanner} appearance="outline" color="success" size="small">
                  {titleCase(scanner)}
                </Badge>
              ))}
              {assessment.scanCoverage.scannersFailed.map((scanner) => (
                <Badge key={scanner} appearance="outline" color="danger" size="small">
                  {titleCase(scanner)} failed
                </Badge>
              ))}
            </div>
          </section>

          <section className="content-section" id="technology" aria-labelledby="technology-heading">
            <div className="section-heading">
              <div>
                <p className="eyebrow">Repository inventory</p>
                <h3 id="technology-heading">Technology</h3>
              </div>
            </div>
            <div className="technology-grid">
              {technologyGroups.map(([label, key]) => {
                const values = assessment.technology[key];
                return (
                  <div className="technology-group" key={key}>
                    <h4>{label}</h4>
                    {values.length > 0 ? (
                      <div className="tag-list">
                        {values.map((value) => <span className="technology-tag" key={value}>{value}</span>)}
                      </div>
                    ) : <span className="not-detected">Not detected</span>}
                  </div>
                );
              })}
            </div>
          </section>

          <section className="content-section" id="dependencies" aria-labelledby="dependencies-heading">
            <div className="section-heading">
              <div>
                <p className="eyebrow">Package and binary evidence</p>
                <h3 id="dependencies-heading">Dependencies</h3>
              </div>
              <span>{pluralize(assessment.dependencies.length, 'item')}</span>
            </div>
            <div className="table-frame">
              <table
                className={assessment.dependencies.length === 0 ? 'empty-table-layout' : undefined}
                aria-label="Dependency findings"
              >
                <thead>
                  <tr>
                    <th scope="col">Dependency</th>
                    <th scope="col">Kind</th>
                    <th scope="col">Architecture</th>
                    <th scope="col">Evidence</th>
                  </tr>
                </thead>
                <tbody>
                  {assessment.dependencies.length === 0 ? (
                    <EmptyRow columns={4} message="No dependencies were reported." />
                  ) : assessment.dependencies.map((dependency) => {
                    const evidence = dependency.evidence[0];
                    return (
                      <tr key={dependency.evidenceId}>
                        <td>
                          <strong>{dependency.name}</strong>
                          <span className="cell-secondary">
                            {dependency.version ?? 'Version not resolved'}
                          </span>
                        </td>
                        <td>
                          <span>{titleCase(dependency.ecosystem)}</span>
                          <span className="cell-secondary">
                            {titleCase(dependency.type)} / {dependency.criticality}
                          </span>
                        </td>
                        <td>
                          <StatusBadge meta={architectureMeta(dependency.architectureStatus)} />
                          <span className="cell-secondary">
                            {dependency.availableArchitectures.length > 0
                              ? dependency.availableArchitectures.join(', ')
                              : 'No architecture evidence'}
                          </span>
                        </td>
                        <td>
                          <span>{evidence?.path ?? evidence?.artifact ?? 'Assessment evidence'}</span>
                          <span className="cell-secondary">{Math.round(dependency.confidence * 100)}% confidence</span>
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          </section>

          <section className="content-section" id="code-findings" aria-labelledby="code-heading">
            <div className="section-heading">
              <div>
                <p className="eyebrow">Compatibility-sensitive patterns</p>
                <h3 id="code-heading">Code findings</h3>
              </div>
              <span>{pluralize(assessment.codeFindings.length, 'finding')}</span>
            </div>
            <div className="table-frame">
              <table
                className={assessment.codeFindings.length === 0 ? 'empty-table-layout' : undefined}
                aria-label="Code compatibility findings"
              >
                <thead>
                  <tr>
                    <th scope="col">Rule and location</th>
                    <th scope="col">Severity</th>
                    <th scope="col">Finding</th>
                    <th scope="col">Confidence</th>
                  </tr>
                </thead>
                <tbody>
                  {assessment.codeFindings.length === 0 ? (
                    <EmptyRow columns={4} message="No compatibility-sensitive code was reported." />
                  ) : assessment.codeFindings.map((finding) => (
                    <tr key={finding.evidenceId}>
                      <td>
                        <strong>{finding.file}</strong>
                        <span className="cell-secondary">
                          {finding.ruleId}{finding.line != null ? ` / line ${finding.line}` : ''}
                        </span>
                      </td>
                      <td><StatusBadge meta={severityMeta(finding.severity)} /></td>
                      <td>
                        <span>{finding.description}</span>
                        <span className="cell-secondary">{titleCase(finding.category)}</span>
                      </td>
                      <td>{Math.round(finding.confidence * 100)}%</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </section>

          <section className="content-section" id="platform-signals" aria-labelledby="platform-heading">
            <div className="section-heading">
              <div>
                <p className="eyebrow">Build and Windows evidence</p>
                <h3 id="platform-heading">Platform signals</h3>
              </div>
            </div>
            <div className="signal-columns">
              <div className="signal-group">
                <h4>Build configuration</h4>
                <SignalRow label="ARM64 target" available={assessment.buildFindings.arm64TargetExists} />
                <SignalRow label="ARM64EC target" available={assessment.buildFindings.arm64EcTargetExists} />
                <SignalRow label="ARM64 CI job" available={assessment.buildFindings.arm64CiJobExists} />
                <SignalRow label="ARM64 packaging" available={assessment.buildFindings.packagingSupportsArm64} />
                <SignalRow label="Tests" available={assessment.buildFindings.testsExist} />
                <div className="signal-context">
                  <span>Detected targets</span>
                  <strong>
                    {assessment.buildFindings.detectedTargets.length > 0
                      ? assessment.buildFindings.detectedTargets.join(', ')
                      : 'None'}
                  </strong>
                </div>
              </div>
              <div className="signal-group">
                <h4>Windows experience</h4>
                <SignalRow label="Windows version" available={assessment.windowsExperience.windowsVersionExists} />
                <SignalRow label="Installer" available={assessment.windowsExperience.installerExists} />
                <SignalRow label="Offline capability" available={assessment.windowsExperience.offlineCapable} />
                <SignalRow label="Notifications" available={assessment.windowsExperience.notificationsIntegrated} />
                <SignalRow label="App lifecycle" available={assessment.windowsExperience.lifecycleIntegrated} />
                <div className="signal-context">
                  <span>UI / accessibility</span>
                  <strong>
                    {titleCase(assessment.windowsExperience.uiTechnology)} / {titleCase(assessment.windowsExperience.accessibilityEvidence)}
                  </strong>
                </div>
              </div>
            </div>
          </section>

          <section className="content-section" id="unknowns" aria-labelledby="unknowns-heading">
            <div className="section-heading">
              <div>
                <p className="eyebrow">Requires verification</p>
                <h3 id="unknowns-heading">Unknowns</h3>
              </div>
              <span>{assessment.unknowns.length} open</span>
            </div>
            {assessment.unknowns.length === 0 ? (
              <p className="empty-state-inline">No unresolved assessment items were reported.</p>
            ) : (
              <div className="unknown-list">
                {assessment.unknowns.map((unknown, index) => (
                  <div className="unknown-row" key={`${unknown.area}-${index}`}>
                    <Badge appearance="outline" color="warning" size="small">
                      {titleCase(unknown.area)}
                    </Badge>
                    <span>{unknown.description}</span>
                    {unknown.requiredSkill ? <strong>{titleCase(unknown.requiredSkill)}</strong> : null}
                  </div>
                ))}
              </div>
            )}
          </section>
        </div>
      </div>
    </div>
  );
}

export default function App() {
  const [source, setSource] = useState('');
  const [assessment, setAssessment] = useState<RepositoryAssessment | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [inputError, setInputError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [authenticationMessage, setAuthenticationMessage] = useState<string | null>(null);
  const controllerRef = useRef<AbortController | null>(null);
  const authenticationSessionRef = useRef<string | null>(null);

  async function authenticateAndRetry(
    normalizedSource: string,
    signal: AbortSignal,
  ) {
    setAuthenticationMessage('Opening GitHub sign-in in your browser.');
    let session = await startGitHubAuthentication(signal);
    authenticationSessionRef.current = session.sessionId;
    setAuthenticationMessage(session.message ?? 'Complete GitHub sign-in in your browser.');

    try {
      while (session.status === 'pending') {
        session = await getGitHubAuthentication(session.sessionId, signal);
        setAuthenticationMessage(session.message ?? 'Waiting for GitHub sign-in.');
        if (session.status === 'pending') {
          await waitForPoll(signal);
        }
      }

      if (session.status !== 'succeeded') {
        throw new Error(session.message ?? 'GitHub sign-in did not complete.');
      }

      return await assessRepository(normalizedSource, signal, session.sessionId);
    } finally {
      if (authenticationSessionRef.current === session.sessionId) {
        authenticationSessionRef.current = null;
      }
    }
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const normalizedSource = source.trim();

    if (!normalizedSource) {
      setInputError('Enter a GitHub repository URL.');
      return;
    }

    setInputError(null);
    setError(null);
    setAssessment(null);
    setAuthenticationMessage(null);
    setLoading(true);
    const nextController = new AbortController();
    controllerRef.current = nextController;

    try {
      let result: RepositoryAssessment;
      try {
        result = await assessRepository(normalizedSource, nextController.signal);
      } catch (requestError) {
        if (!(requestError instanceof AssessmentApiError && requestError.authenticationRequired)) {
          throw requestError;
        }

        result = await authenticateAndRetry(normalizedSource, nextController.signal);
      }

      setAuthenticationMessage(null);
      setAssessment(result);
    } catch (requestError) {
      if (!(requestError instanceof DOMException && requestError.name === 'AbortError')) {
        setError(requestError instanceof Error ? requestError.message : 'Assessment failed.');
      }
    } finally {
      setLoading(false);
      if (controllerRef.current === nextController) {
        controllerRef.current = null;
      }
    }
  }

  function cancelAssessment() {
    const authenticationSessionId = authenticationSessionRef.current;
    authenticationSessionRef.current = null;
    controllerRef.current?.abort();
    if (authenticationSessionId) {
      void cancelGitHubAuthentication(authenticationSessionId).catch(() => undefined);
    }
  }

  return (
    <FluentProvider theme={webLightTheme} className="app-provider">
      <header className="global-header">
        <div className="global-header-inner">
          <a className="brand" href="#top" aria-label="Arm Migration Assist home">
            <span className="brand-mark" aria-hidden="true">
              <i /><i /><i /><i />
            </span>
            <span>Arm Migration Assist</span>
          </a>
          <Badge appearance="tint" color="brand">Feature 1</Badge>
        </div>
      </header>

      <main id="top">
        <section className="intake-band" aria-labelledby="page-title">
          <div className="page-intro">
            <p className="eyebrow">Windows on Arm</p>
            <h1 id="page-title">Repository assessment</h1>
            <p className="page-context">Evidence workspace for GitHub repositories</p>
          </div>

          <form className="assessment-form" onSubmit={handleSubmit} noValidate>
            <Field
              className="repository-field"
              label="GitHub repository URL"
              validationMessage={inputError}
              validationState={inputError ? 'error' : 'none'}
            >
              <Input
                value={source}
                onChange={(_, data) => {
                  setSource(data.value);
                  if (inputError) setInputError(null);
                }}
                contentBefore={<Search20Regular />}
                placeholder="https://github.com/owner/repository"
                size="large"
                type="url"
                disabled={loading}
              />
            </Field>
            <div className="form-actions">
              <Button
                appearance="primary"
                icon={loading ? <Spinner size="tiny" /> : <ArrowRight20Regular />}
                iconPosition="after"
                size="large"
                type="submit"
                disabled={loading}
              >
                {loading ? 'Assessing repository' : 'Run assessment'}
              </Button>
              {loading ? (
                <Button
                  appearance="subtle"
                  icon={<Dismiss20Regular />}
                  size="large"
                  type="button"
                  onClick={cancelAssessment}
                >
                  Cancel
                </Button>
              ) : null}
            </div>
          </form>

          {error ? (
            <MessageBar className="request-message" intent="error">
              <MessageBarBody>{error}</MessageBarBody>
            </MessageBar>
          ) : null}
          {loading ? (
            <div className="request-status" role="status" aria-live="polite">
              <span>{authenticationMessage ? 'Waiting for GitHub sign-in' : 'Downloading and scanning a read-only snapshot'}</span>
              <span>
                {authenticationMessage ?? 'Results will appear when evidence collection is complete.'}
              </span>
            </div>
          ) : null}
        </section>

        <div className="workspace">
          {assessment ? (
            <AssessmentResults assessment={assessment} />
          ) : !loading ? (
            <section className="empty-workspace" aria-label="Assessment status">
              <div className="empty-glyph" aria-hidden="true"><span /></div>
              <h2>Awaiting repository</h2>
              <p>No assessment results are loaded.</p>
            </section>
          ) : null}
        </div>
      </main>

      <footer>
        <span>Arm Migration Assist</span>
        <span>Repository discovery and compatibility evidence</span>
      </footer>
    </FluentProvider>
  );
}