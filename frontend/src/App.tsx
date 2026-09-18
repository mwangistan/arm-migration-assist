import { useRef, useState, type FormEvent } from 'react';
import {
  Badge,
  Button,
  Field,
  FluentProvider,
  Input,
  Menu,
  MenuItem,
  MenuList,
  MenuPopover,
  MenuTrigger,
  MessageBar,
  MessageBarBody,
  ProgressBar,
  Spinner,
  webLightTheme,
} from '@fluentui/react-components';
import {
  ArrowDownload20Regular,
  ArrowRight20Regular,
  Checkmark16Regular,
  Dismiss20Regular,
  Print20Regular,
  Search20Regular,
} from '@fluentui/react-icons';
import {
  AssessmentApiError,
  assessRepositoryWithProgress,
  cancelAssessmentJob,
  cancelGitHubAuthentication,
  getGitHubAuthentication,
  getValidationReport,
  migrationReportUrl,
  planMigration,
  pollArm64Run,
  pollMigrationJob,
  pollValidationRun,
  startGitHubAuthentication,
  submitMigrationJob,
  type AssessmentJob,
  type AssessmentProgress,
} from './api';
import type {
  Arm64BuildDispatch,
  Arm64RunStatus,
  Arm64StepOutcome,
  CodeFinding,
  CriterionResult,
  DependencyFinding,
  GeneratedPatch,
  MigrationJob,
  MigrationPlanningResult,
  MigrationWorkItem,
  RepositoryAssessment,
  ValidationReport,
  ValidationRunSummary,
} from './types';

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

const githubRepositorySegment = /^[A-Za-z0-9_.-]+$/;

function isGitHubRepositoryUrl(value: string) {
  try {
    const url = new URL(value);
    const segments = url.pathname.split('/').filter(Boolean);
    const repository = segments[1]?.replace(/\.git$/i, '') ?? '';

    return url.protocol === 'https:'
      && url.hostname.toLowerCase() === 'github.com'
      && url.port === ''
      && url.username === ''
      && url.password === ''
      && url.search === ''
      && url.hash === ''
      && segments.length === 2
      && githubRepositorySegment.test(segments[0])
      && githubRepositorySegment.test(repository);
  } catch {
    return false;
  }
}

function pluralize(count: number, singular: string, plural = `${singular}s`) {
  return `${count} ${count === 1 ? singular : plural}`;
}

function titleCase(value: string) {
  return value
    .replace(/[-_]/g, ' ')
    .replace(/\b\w/g, (character) => character.toUpperCase())
    .replace(/\bArm64ec\b/g, 'ARM64EC')
    .replace(/\bArm64\b/g, 'ARM64')
    .replace(/\bCi\b/g, 'CI')
    .replace(/\bApi\b/g, 'API')
    .replace(/\bSdk\b/g, 'SDK');
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

function downloadJson(value: unknown, fileName: string) {
  const blob = new Blob([JSON.stringify(value, null, 2)], { type: 'application/json' });
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = fileName;
  anchor.click();
  URL.revokeObjectURL(url);
}

function downloadReport(runId: string, extension: 'md' | 'html') {
  const anchor = document.createElement('a');
  anchor.href = migrationReportUrl(runId, extension);
  anchor.download = `migration-report-${runId}.${extension}`;
  anchor.click();
}

function workItemColor(priority: string): BadgeColor {
  if (priority === 'P0') return 'danger';
  if (priority === 'P1') return 'warning';
  return 'informative';
}

type WorkflowStageState = 'active' | 'attention' | 'complete' | 'ready' | 'waiting';

function ProductWorkflow({
  assessment,
  planning,
  planningError,
  loading,
  progressPhase,
  migrationJob,
  migrationJobPending,
  migrationJobError,
  validationRun,
  validationReport,
  validationPending,
}: {
  assessment: RepositoryAssessment | null;
  planning: MigrationPlanningResult | null;
  planningError: string | null;
  loading: boolean;
  progressPhase: string;
  migrationJob: MigrationJob | null;
  migrationJobPending: boolean;
  migrationJobError: string | null;
  validationRun: ValidationRunSummary | null;
  validationReport: ValidationReport | null;
  validationPending: boolean;
}) {
  const actionableSkills = new Set([
    'build/add-arm64-target',
    'pipeline/github-actions-arm64-job',
    'code/arch-conditional-cleanup',
  ]);
  const actionableWork = planning?.plan.workItems.filter((item) =>
    actionableSkills.has(item.agentOrSkill)).length ?? 0;
  const validationChecks = planning
    ? Object.entries(planning.plan.validationPlan)
        .filter(([key, value]) => key !== 'targetDevices' && Array.isArray(value))
        .reduce((total, [, value]) => total + value.length, 0)
    : 0;
  const assessing = loading && progressPhase !== 'migration-planning';
  const job = migrationJob;
  const jobFailed = migrationJobError !== null || job?.status === 'failed';
  const transformDetail = job?.status === 'completed'
    ? `${pluralize(job.result?.generated.length ?? 0, 'patch', 'patches')} generated`
    : migrationJobPending
      ? job?.status === 'running' ? 'Generating patches' : 'Queued'
      : jobFailed
        ? 'Run failed'
        : planning
          ? `${actionableWork} ready / ${planning.plan.missingSkills.length} gaps`
          : 'Reviewable patches';
  const transformState: WorkflowStageState = job?.status === 'completed'
    ? 'complete'
    : jobFailed
      ? 'attention'
      : migrationJobPending
        ? 'active'
        : planning
          ? actionableWork > 0 ? 'ready' : 'attention'
          : 'waiting';
  const scorecard = validationReport?.scorecard ?? null;
  const validationFailed = validationRun?.status === 'failed';
  const validateDetail = scorecard
    ? scorecard.status === 'validated'
      ? `${scorecard.passed} passed`
      : scorecard.status === 'validation-failed'
        ? `${scorecard.failed} failed`
        : scorecard.status === 'partially-validated'
          ? `${scorecard.passed}/${scorecard.criteria.length} passed`
          : 'Not validated'
    : validationPending
      ? validationRun?.status === 'running' ? 'Validation running' : 'Validation queued'
      : validationFailed
        ? 'Validation failed'
        : planning
          ? `${pluralize(validationChecks, 'check')} defined`
          : 'ARM64 verification';
  const validateState: WorkflowStageState = scorecard
    ? scorecard.status === 'validated'
      ? 'complete'
      : scorecard.status === 'validation-failed' ? 'attention' : 'ready'
    : validationFailed
      ? 'attention'
      : validationPending
        ? 'active'
        : planning ? 'ready' : 'waiting';
  const stages: Array<{
    label: string;
    detail: string;
    state: WorkflowStageState;
  }> = [
    {
      label: 'Connect',
      detail: assessment || loading ? 'Repository selected' : 'GitHub repository',
      state: assessment || loading ? 'complete' : 'active',
    },
    {
      label: 'Assess',
      detail: assessment ? `${assessment.scanCoverage.filesScanned} files analyzed` : assessing ? titleCase(progressPhase) : 'Evidence collection',
      state: assessment ? 'complete' : assessing ? 'active' : 'waiting',
    },
    {
      label: 'Plan',
      detail: planning ? titleCase(planning.plan.recommendedPath) : planningError ? 'Review required' : progressPhase === 'migration-planning' ? 'Scoring readiness' : 'Migration strategy',
      state: planning ? 'complete' : planningError ? 'attention' : progressPhase === 'migration-planning' ? 'active' : 'waiting',
    },
    {
      label: 'Transform',
      detail: transformDetail,
      state: transformState,
    },
    {
      label: 'Validate',
      detail: validateDetail,
      state: validateState,
    },
  ];

  return (
    <section className="workflow-band" id="workflow" aria-label="Migration workflow">
      <ol className="workflow-steps">
        {stages.map((stage, index) => (
          <li className={`workflow-step workflow-${stage.state}`} key={stage.label}>
            <span className="workflow-index" aria-hidden="true">
              {stage.state === 'complete' ? <Checkmark16Regular /> : index + 1}
            </span>
            <span>
              <strong>{stage.label}</strong>
              <small>{stage.detail}</small>
            </span>
          </li>
        ))}
      </ol>
    </section>
  );
}

function PlanningResults({
  planning,
  pending,
  error,
}: {
  planning: MigrationPlanningResult | null;
  pending: boolean;
  error: string | null;
}) {
  if (pending) {
    return (
      <section className="content-section planning-section" id="migration-plan" aria-labelledby="planning-heading">
        <div className="section-heading">
          <div>
            <p className="eyebrow">Migration planning</p>
            <h3 id="planning-heading">Building the migration path</h3>
          </div>
          <Spinner size="small" label="Planning" />
        </div>
        <ProgressBar aria-label="Migration planning in progress" />
      </section>
    );
  }

  if (error) {
    return (
      <section className="content-section planning-section" id="migration-plan" aria-labelledby="planning-heading">
        <div className="section-heading">
          <div>
            <p className="eyebrow">Migration planning</p>
            <h3 id="planning-heading">Plan unavailable</h3>
          </div>
        </div>
        <MessageBar intent="error"><MessageBarBody>{error}</MessageBarBody></MessageBar>
      </section>
    );
  }

  if (!planning) return null;

  const { plan, score, warnings } = planning;
  const validationGroups = [
    ['Build', plan.validationPlan.buildChecks],
    ['Functional', plan.validationPlan.functionalChecks],
    ['Reliability', plan.validationPlan.reliabilityChecks],
    ['Performance', plan.validationPlan.performanceChecks],
    ['Power', plan.validationPlan.powerChecks],
    ['Offline', plan.validationPlan.offlineChecks],
    ['Accessibility', plan.validationPlan.accessibilityChecks],
    ['Windows experience', plan.validationPlan.windowsExperienceChecks],
  ] as const;
  const validationChecks = validationGroups.flatMap(([group, checks]) =>
    checks.map((check) => ({ group, check })));
  return (
    <section className="content-section planning-section" id="migration-plan" aria-labelledby="planning-heading">
      <div className="section-heading">
        <div>
          <p className="eyebrow">Migration planning</p>
          <h3 id="planning-heading">Recommended path</h3>
        </div>
        <StatusBadge meta={{
          label: score.provisional ? 'Provisional' : titleCase(plan.confidence),
          color: score.provisional ? 'warning' : 'success',
        }} />
      </div>

      <div className="planning-summary">
        <div className="readiness-score" aria-label={`Readiness score ${score.overallScore} out of 100`}>
          <span className="score-value">{score.overallScore}</span>
          <span className="score-total">/ 100</span>
          <strong>{titleCase(score.band)}</strong>
          <span>{Math.round(score.evidenceCompletenessScore * 100)}% evidence completeness</span>
        </div>
        <div className="recommendation-copy">
          <div className="recommendation-title">
            <span>Recommended</span>
            <strong>{titleCase(plan.recommendedPath)}</strong>
          </div>
          <p>{plan.executiveSummary}</p>
        </div>
      </div>

      <div className="dimension-grid" aria-label="Readiness dimensions">
        {score.dimensions.map((dimension) => (
          <div className="dimension" key={dimension.dimensionKey}>
            <div>
              <span>{titleCase(dimension.dimensionKey)}</span>
              <strong>{dimension.rawScore}</strong>
            </div>
            <ProgressBar value={dimension.rawScore / 100} aria-label={`${titleCase(dimension.dimensionKey)} ${dimension.rawScore}`} />
            <small>{dimension.weightPct}% weight</small>
          </div>
        ))}
      </div>

      {score.majorBlockers.length > 0 ? (
        <div className="blocker-list" aria-label="Major blockers">
          {score.majorBlockers.map((blocker) => (
            <div key={blocker.blockerId}>
              <StatusBadge meta={{ label: titleCase(blocker.category), color: 'danger' }} />
              <span>{blocker.description}</span>
            </div>
          ))}
        </div>
      ) : null}

      {warnings.length > 0 ? (
        <section className="plan-provenance" aria-label="Plan provenance">
          <p className="eyebrow">Plan provenance</p>
          <p className="plan-provenance-context">
            The pipeline recorded these observations while producing the plan. They describe how the plan was built (retries, deterministic synthesis, corpus tool calls) — none of them require reviewer action.
          </p>
          <ul>
            {warnings.map((warning, index) => (
              <li key={`${warning}-${index}`}>{warning}</li>
            ))}
          </ul>
        </section>
      ) : null}

      <div className="work-items-heading">
        <h4>Migration work</h4>
        <span>{pluralize(plan.workItems.length, 'work item')}</span>
      </div>
      {plan.workItems.length === 0 ? (
        <p className="empty-state-inline">No migration work items were generated.</p>
      ) : (
        <div className="work-item-list">
          {plan.workItems.map((item) => <WorkItemRow item={item} key={item.id} />)}
        </div>
      )}

      <div className="decision-grid">
        <section aria-labelledby="alternatives-heading">
          <h4 id="alternatives-heading">Alternatives</h4>
          {plan.alternatives.map((alternative) => (
            <div className="decision-row" key={alternative.path}>
              <div>
                <strong>{titleCase(alternative.path)}</strong>
                <StatusBadge meta={{
                  label: titleCase(alternative.disposition),
                  color: alternative.disposition === 'viable' ? 'success' : 'subtle',
                }} />
              </div>
              <p>{alternative.rationale}</p>
            </div>
          ))}
        </section>

        <section aria-labelledby="risks-heading">
          <h4 id="risks-heading">Risks and unknowns</h4>
          {plan.risks.map((risk) => (
            <div className="decision-row" key={risk.id}>
              <div>
                <strong>{risk.description}</strong>
                <StatusBadge meta={{ label: titleCase(risk.severity), color: risk.severity === 'critical' ? 'danger' : 'warning' }} />
              </div>
              <p>{risk.mitigation}</p>
            </div>
          ))}
          {plan.unknowns.map((unknown) => (
            <div className="decision-row" key={unknown.id}>
              <strong>{unknown.description}</strong>
              {unknown.requiredSkill ? <p>Resolve with {titleCase(unknown.requiredSkill)}.</p> : null}
            </div>
          ))}
          {plan.risks.length === 0 && plan.unknowns.length === 0 ? (
            <p className="empty-state-inline">No additional planning risks or unknowns.</p>
          ) : null}
        </section>

        <section aria-labelledby="gates-heading">
          <h4 id="gates-heading">Capability and approval gates</h4>
          {plan.missingSkills.map((skill) => (
            <div className="decision-row" key={skill.proposedName}>
              <div>
                <strong>{titleCase(skill.proposedName)}</strong>
                <StatusBadge meta={{ label: 'Missing skill', color: 'warning' }} />
              </div>
              <p>{skill.purpose}</p>
            </div>
          ))}
          {plan.requiredApprovals.map((approval) => (
            <div className="decision-row" key={approval.approvalId}>
              <div>
                <strong>{approval.summary}</strong>
                <StatusBadge meta={{ label: 'Approval required', color: 'important' }} />
              </div>
              <p>{pluralize(approval.workItemIds.length, 'work item')} gated.</p>
            </div>
          ))}
        </section>
      </div>

      <div className="validation-plan">
        <div className="work-items-heading">
          <h4>ARM64 validation plan</h4>
          <span>{plan.validationPlan.targetDevices.map(titleCase).join(', ')}</span>
        </div>
        {validationChecks.length === 0 ? (
          <p className="empty-state-inline">No additional validation checks were generated.</p>
        ) : (
          <div className="validation-list">
            {validationChecks.map(({ group, check }) => (
              <div key={check.id}>
                <Badge appearance="outline" color="informative" size="small">{group}</Badge>
                <p><strong>{check.description}</strong>{check.expectedOutcome}</p>
              </div>
            ))}
          </div>
        )}
      </div>
    </section>
  );
}

function WorkItemRow({ item }: { item: MigrationWorkItem }) {
  return (
    <article className="work-item">
      <div className="work-item-index">{String(item.sequence).padStart(2, '0')}</div>
      <div className="work-item-body">
        <div className="work-item-title">
          <div>
            <Badge appearance="tint" color={workItemColor(item.priority)} size="small">{item.priority}</Badge>
            <Badge appearance="outline" color="informative" size="small">{titleCase(item.agentOrSkill)}</Badge>
          </div>
          <span>{titleCase(item.estimatedEffort)} effort / {titleCase(item.risk)} risk</span>
        </div>
        <h5>{item.title}</h5>
        <p>{item.objective}</p>
        <div className="acceptance-list">
          {item.acceptanceTests.map((test) => (
            <div key={test.id}>
              <Checkmark16Regular aria-hidden="true" />
              <p><strong>{test.description}</strong>{test.expectedOutcome}</p>
            </div>
          ))}
        </div>
      </div>
    </article>
  );
}

function migrationJobLabel(job: MigrationJob | null, pending: boolean) {
  if (pending && (!job || job.status === 'queued' || job.status === 'running')) {
    return job?.status === 'running' ? 'Generating patches' : 'Queued';
  }
  if (!job) return 'Ready';
  if (job.status === 'completed') return 'Patches ready';
  if (job.status === 'failed') return 'Run failed';
  return titleCase(job.status);
}

function migrationJobBadgeColor(job: MigrationJob | null, pending: boolean): BadgeColor {
  if (pending) return 'informative';
  if (!job) return 'subtle';
  if (job.status === 'completed') return 'success';
  if (job.status === 'failed') return 'danger';
  return 'informative';
}

function validationStatusMeta(status: string): StatusMeta {
  switch (status) {
    case 'validated': return { label: 'Validated', color: 'success' };
    case 'validation-failed': return { label: 'Validation failed', color: 'danger' };
    case 'partially-validated': return { label: 'Partially validated', color: 'warning' };
    default: return { label: 'Not validated', color: 'subtle' };
  }
}

function criterionStatusMeta(status: CriterionResult['status']): StatusMeta {
  switch (status) {
    case 'passed': return { label: 'Passed', color: 'success' };
    case 'failed': return { label: 'Failed', color: 'danger' };
    case 'inconclusive': return { label: 'Inconclusive', color: 'warning' };
    case 'skipped': return { label: 'Skipped', color: 'subtle' };
    default: return { label: 'Not run', color: 'subtle' };
  }
}

function GeneratedPatchRow({ patch }: { patch: GeneratedPatch }) {
  const label = `${patch.originalSizeBytes.toLocaleString()} bytes`;
  return (
    <details className="patch-row">
      <summary>
        <div>
          <Badge appearance="outline" color="informative" size="small">
            {titleCase(patch.agentOrSkill)}
          </Badge>
          <strong>{patch.title}</strong>
        </div>
        <span>
          {label}{patch.truncated ? ' (truncated)' : ''}
        </span>
      </summary>
      <pre className="patch-diff" aria-label={`Diff for ${patch.title}`}>{patch.diff}</pre>
    </details>
  );
}

function TransformValidateResults({
  planning,
  target,
  job,
  jobPending,
  jobError,
  validationRun,
  validationReport,
  validationPending,
  validationError,
  arm64Run,
  arm64Pending,
  arm64Error,
  onRun,
  onCancel,
}: {
  planning: MigrationPlanningResult;
  target: { url: string; commitSha: string } | null;
  job: MigrationJob | null;
  jobPending: boolean;
  jobError: string | null;
  validationRun: ValidationRunSummary | null;
  validationReport: ValidationReport | null;
  validationPending: boolean;
  validationError: string | null;
  arm64Run: Arm64RunStatus | null;
  arm64Pending: boolean;
  arm64Error: string | null;
  onRun: () => void;
  onCancel: () => void;
}) {
  const result = job?.result ?? null;
  const generated = result?.generated ?? [];
  const skipped = result?.skipped ?? [];
  const branch = result?.branch ?? null;
  const dispatch = result?.validation ?? null;
  const arm64Dispatch = result?.arm64Build ?? null;
  const scorecard = validationReport?.scorecard ?? null;
  const canRun = Boolean(target) && !jobPending;

  return (
    <section
      className="content-section transform-section"
      id="migration-actions"
      aria-labelledby="transform-heading"
    >
      <div className="section-heading">
        <div>
          <p className="eyebrow">Transform + validate</p>
          <h3 id="transform-heading">Reviewable changes</h3>
        </div>
        <StatusBadge meta={{
          label: migrationJobLabel(job, jobPending),
          color: migrationJobBadgeColor(job, jobPending),
        }} />
      </div>

      <p className="transform-context">
        Runs the runnable work items in the plan against a fresh clone of the pinned commit,
        applies the resulting patches to a scratch branch, and dispatches the ARM64 validation
        run. Nothing is pushed to your repository.
      </p>

      <div className="transform-actions">
        <Button
          appearance="primary"
          onClick={onRun}
          disabled={!canRun}
          icon={jobPending ? <Spinner size="tiny" /> : <ArrowRight20Regular />}
          iconPosition="after"
        >
          {jobPending
            ? job?.status === 'running' ? 'Generating patches' : 'Queued'
            : job?.status === 'completed' ? 'Rerun migration actions' : 'Generate reviewable changes'}
        </Button>
        {jobPending ? (
          <Button appearance="secondary" onClick={onCancel} icon={<Dismiss20Regular />}>
            Cancel
          </Button>
        ) : null}
        <span className="transform-plan-id">Plan {planning.plan.planId}</span>
      </div>

      {jobPending ? <ProgressBar aria-label="Migration actions in progress" /> : null}

      {jobError ? (
        <MessageBar intent="error"><MessageBarBody>{jobError}</MessageBarBody></MessageBar>
      ) : null}

      {job?.status === 'failed' && job.error ? (
        <MessageBar intent="error"><MessageBarBody>{job.error}</MessageBarBody></MessageBar>
      ) : null}

      {result ? (
        <div className="transform-summary">
          <div className="transform-summary-grid">
            <div>
              <span className="metric-label">Patches generated</span>
              <span className="metric-value">{generated.length}</span>
            </div>
            <div>
              <span className="metric-label">Skipped</span>
              <span className="metric-value">{skipped.length}</span>
            </div>
            <div>
              <span className="metric-label">Applied</span>
              <span className="metric-value">{branch?.appliedIds.length ?? 0}</span>
            </div>
            <div>
              <span className="metric-label">Rejected</span>
              <span className="metric-value">{branch?.rejected.length ?? 0}</span>
            </div>
          </div>

          {branch ? (
            <div className="transform-branch">
              <p>
                <strong>Branch </strong>
                <code>{branch.branchName}</code>
                {branch.commitCreated ? (
                  <>
                    {' at '}
                    <code title={branch.branchHeadSha}>{branch.branchHeadSha.slice(0, 12)}</code>
                  </>
                ) : (
                  <span> (no commit — all patches rejected)</span>
                )}
              </p>
              {branch.rejected.length > 0 ? (
                <ul className="rejection-list" aria-label="Rejected patches">
                  {branch.rejected.map((rejection) => (
                    <li key={rejection.id}>
                      <code>{rejection.id}</code>
                      <span>{rejection.reason}</span>
                    </li>
                  ))}
                </ul>
              ) : null}
            </div>
          ) : null}

          {generated.length > 0 ? (
            <div className="patch-list" aria-label="Generated patches">
              {generated.map((patch) => (
                <GeneratedPatchRow key={patch.workItemId} patch={patch} />
              ))}
            </div>
          ) : null}

          {skipped.length > 0 ? (
            <div className="skipped-list" aria-label="Skipped work items">
              <h4>Skipped work items</h4>
              <ul>
                {skipped.map((item) => (
                  <li key={item.workItemId}>
                    <div>
                      <Badge appearance="outline" color="subtle" size="small">
                        {titleCase(item.agentOrSkill)}
                      </Badge>
                      <span>{item.workItemId}</span>
                    </div>
                    <p>{item.reason}</p>
                  </li>
                ))}
              </ul>
            </div>
          ) : null}
        </div>
      ) : null}

      {dispatch ? (
        <div className="validation-panel" aria-labelledby="validation-heading">
          <div className="section-heading">
            <div>
              <p className="eyebrow">ARM64 validation</p>
              <h4 id="validation-heading">Validation run</h4>
            </div>
            <StatusBadge meta={
              scorecard
                ? validationStatusMeta(scorecard.status)
                : {
                    label: validationPending
                      ? validationRun?.status === 'running' ? 'Running' : 'Queued'
                      : titleCase(validationRun?.status ?? 'queued'),
                    color: validationPending ? 'informative' : 'subtle',
                  }
            } />
          </div>

          {!dispatch.dispatched ? (
            <MessageBar intent="warning">
              <MessageBarBody>
                Validation could not be dispatched.
                {dispatch.error ? ` ${dispatch.error}` : ''}
              </MessageBarBody>
            </MessageBar>
          ) : null}

          {validationPending ? <ProgressBar aria-label="Validation run in progress" /> : null}

          {validationError ? (
            <MessageBar intent="error"><MessageBarBody>{validationError}</MessageBarBody></MessageBar>
          ) : null}

          {validationRun?.status === 'failed' && validationRun.error ? (
            <MessageBar intent="error"><MessageBarBody>{validationRun.error}</MessageBarBody></MessageBar>
          ) : null}

          {scorecard ? (
            <>
              <div className="scorecard-grid">
                <div><span className="metric-label">Passed</span><span className="metric-value">{scorecard.passed}</span></div>
                <div><span className="metric-label">Failed</span><span className="metric-value">{scorecard.failed}</span></div>
                <div><span className="metric-label">Inconclusive</span><span className="metric-value">{scorecard.inconclusive}</span></div>
                <div><span className="metric-label">Not run</span><span className="metric-value">{scorecard.notRun}</span></div>
                <div><span className="metric-label">Skipped</span><span className="metric-value">{scorecard.skipped}</span></div>
              </div>

              <div className="criteria-list" aria-label="Validation criteria">
                {scorecard.criteria.map((criterion) => (
                  <div className="criteria-row" key={criterion.criterion.key}>
                    <div>
                      <Badge appearance="outline" color="informative" size="small">
                        {titleCase(criterion.criterion.category)}
                      </Badge>
                      <StatusBadge meta={criterionStatusMeta(criterion.status)} />
                    </div>
                    <p><strong>{criterion.criterion.description}</strong>{criterion.reason}</p>
                  </div>
                ))}
              </div>

              {validationReport && validationReport.coverageGaps.length > 0 ? (
                <div className="coverage-gaps" aria-label="Coverage gaps">
                  <h5>Coverage gaps</h5>
                  <ul>
                    {validationReport.coverageGaps.map((gap) => (
                      <li key={gap.id}>
                        <strong>{gap.description}</strong>
                        <span>{gap.criterionKeys.join(', ')}</span>
                      </li>
                    ))}
                  </ul>
                </div>
              ) : null}
            </>
          ) : null}
        </div>
      ) : null}

      <Arm64BuildPanel
        dispatch={arm64Dispatch}
        run={arm64Run}
        pending={arm64Pending}
        error={arm64Error}
      />
    </section>
  );
}

function Arm64BuildPanel({
  dispatch,
  run,
  pending,
  error,
}: {
  dispatch: Arm64BuildDispatch | null;
  run: Arm64RunStatus | null;
  pending: boolean;
  error: string | null;
}) {
  if (!dispatch) return null;

  const scorecard = run?.scorecard ?? null;
  const hardware = scorecard?.hardware ?? null;

  const overall: { label: string; color: BadgeColor } = (() => {
    if (!dispatch.dispatched) return { label: 'Not dispatched', color: 'important' };
    if (run?.status === 'failed') return { label: 'Failed', color: 'danger' };
    if (run?.status === 'cancelled') return { label: 'Cancelled', color: 'subtle' };
    if (scorecard) {
      const buildOk = scorecard.build?.succeeded ?? false;
      const testsOk = scorecard.tests?.succeeded ?? true;
      if (buildOk && testsOk) return { label: 'Pass', color: 'success' };
      return { label: 'Fail', color: 'danger' };
    }
    if (pending || run?.status === 'running') return { label: 'Running', color: 'informative' };
    if (run?.status === 'queued') return { label: 'Queued', color: 'subtle' };
    return { label: 'Waiting', color: 'subtle' };
  })();

  const cpuLabel = hardware
    ? `${hardware.cpuCount} vCPU · ${hardware.architecture} · ${hardware.kernel.split(' ').slice(0, 2).join(' ')}`
    : null;
  const skuLabel = hardware
    ? `${hardware.vmSku} · ${friendlyCpuModel(hardware.cpuModel)} · ${hardware.region}`
    : dispatch.dispatched
      ? 'Standard_D4ps_v6 · Ampere Cobalt 100 · eastus2'
      : null;

  return (
    <div className="arm64-panel" aria-labelledby="arm64-heading">
      <div className="section-heading">
        <div>
          <p className="eyebrow">Real ARM64 hardware verification</p>
          <h4 id="arm64-heading">Live build + test on Azure</h4>
        </div>
        <StatusBadge meta={overall} />
      </div>

      {skuLabel ? (
        <div className="arm64-hardware">
          <span className="arm64-hardware-label">Compute</span>
          <span className="arm64-hardware-value">{skuLabel}</span>
          {cpuLabel ? <span className="arm64-hardware-value">{cpuLabel}</span> : null}
          {hardware ? (
            <span className="arm64-hardware-value">Memory {formatMemory(hardware.memoryMB)}</span>
          ) : null}
        </div>
      ) : null}

      {!dispatch.dispatched ? (
        <p className="arm64-note">
          Runner not reached. {dispatch.error ?? 'No further detail.'}
        </p>
      ) : null}

      {error ? <MessageBar intent="error"><MessageBarBody>{error}</MessageBarBody></MessageBar> : null}

      {dispatch.dispatched && !scorecard && (pending || run?.status === 'queued' || run?.status === 'running') ? (
        <>
          <p className="arm64-note">
            Cloning source, applying patches, and running build + test on real ARM64 hardware.
          </p>
          <ProgressBar aria-label="ARM64 build in progress" />
        </>
      ) : null}

      {scorecard ? (
        <>
          <div className="arm64-metrics">
            <div>
              <span className="metric-label">Wall clock</span>
              <span className="metric-value">{formatSeconds(scorecard.wallClockSeconds)}</span>
            </div>
            <div>
              <span className="metric-label">Applied</span>
              <span className="metric-value">
                {scorecard.patchApplication.applied.length}/{scorecard.patchApplication.applied.length + scorecard.patchApplication.rejected.length}
              </span>
            </div>
            <div>
              <span className="metric-label">Base commit</span>
              <span className="metric-value metric-mono">{shortSha(scorecard.sourceCommitSha)}</span>
            </div>
            <div>
              <span className="metric-label">Resolved commit</span>
              <span className="metric-value metric-mono">{shortSha(scorecard.resolvedCommitSha)}</span>
            </div>
          </div>

          {scorecard.patchApplication.rejected.length > 0 ? (
            <div className="arm64-rejects" aria-label="Rejected patches">
              <h5>Patches rejected</h5>
              <ul>
                {scorecard.patchApplication.rejected.map((r) => (
                  <li key={r.id}><strong>{r.id}</strong><span>{r.reason}</span></li>
                ))}
              </ul>
            </div>
          ) : null}

          <Arm64StepDetail label="Build" outcome={scorecard.build} />
          <Arm64StepDetail label="Tests" outcome={scorecard.tests} />

          <p className="arm64-note arm64-summary">{scorecard.summary}</p>
        </>
      ) : null}
    </div>
  );
}

function Arm64StepDetail({ label, outcome }: { label: string; outcome: Arm64StepOutcome | null }) {
  if (!outcome) return null;
  const meta: { label: string; color: BadgeColor } = outcome.succeeded
    ? { label: 'Pass', color: 'success' }
    : { label: 'Fail', color: 'danger' };
  return (
    <details className="arm64-step">
      <summary>
        <StatusBadge meta={meta} />
        <span className="arm64-step-label">{label}</span>
        <span className="arm64-step-duration">{formatSeconds(outcome.durationSeconds)}</span>
        <code className="arm64-step-command">{outcome.command}</code>
      </summary>
      {outcome.stdoutTail ? (
        <>
          <p className="arm64-step-caption">stdout tail</p>
          <pre className="arm64-step-log">{outcome.stdoutTail}</pre>
        </>
      ) : null}
      {outcome.stderrTail ? (
        <>
          <p className="arm64-step-caption">stderr tail</p>
          <pre className="arm64-step-log arm64-step-log-err">{outcome.stderrTail}</pre>
        </>
      ) : null}
    </details>
  );
}

function friendlyCpuModel(cpuModel: string): string {
  const trimmed = (cpuModel ?? '').trim();
  if (!trimmed || trimmed === 'unknown') return 'Ampere Cobalt 100';
  if (/cobalt/i.test(trimmed)) return trimmed;
  if (/ampere/i.test(trimmed)) return trimmed;
  return trimmed.length > 48 ? trimmed.slice(0, 48) + '…' : trimmed;
}

function formatMemory(mb: number): string {
  if (!mb || mb <= 0) return 'unknown';
  const gib = mb / 1024;
  return gib >= 10 ? `${Math.round(gib)} GiB` : `${gib.toFixed(1)} GiB`;
}

function formatSeconds(seconds: number | null | undefined): string {
  if (seconds === null || seconds === undefined || Number.isNaN(seconds)) return '—';
  if (seconds < 1) return `${Math.round(seconds * 1000)} ms`;
  if (seconds < 60) return `${seconds.toFixed(1)} s`;
  const m = Math.floor(seconds / 60);
  const s = Math.round(seconds - m * 60);
  return `${m}m ${s}s`;
}

function shortSha(sha: string | null | undefined): string {
  if (!sha) return '—';
  return sha.length > 10 ? sha.slice(0, 10) : sha;
}

function AssessmentResults({
  assessment,
  planning,
  planningPending,
  planningError,
  migrationJob,
  migrationJobPending,
  migrationJobError,
  validationRun,
  validationReport,
  validationPending,
  validationError,
  arm64Run,
  arm64Pending,
  arm64Error,
  onRunMigration,
  onCancelMigration,
}: {
  assessment: RepositoryAssessment;
  planning: MigrationPlanningResult | null;
  planningPending: boolean;
  planningError: string | null;
  migrationJob: MigrationJob | null;
  migrationJobPending: boolean;
  migrationJobError: string | null;
  validationRun: ValidationRunSummary | null;
  validationReport: ValidationReport | null;
  validationPending: boolean;
  validationError: string | null;
  arm64Run: Arm64RunStatus | null;
  arm64Pending: boolean;
  arm64Error: string | null;
  onRunMigration: () => void;
  onCancelMigration: () => void;
}) {
  const coverage = assessment.scanCoverage.filesTotal === 0
    ? 0
    : assessment.scanCoverage.filesScanned / assessment.scanCoverage.filesTotal;
  const commitSha = assessment.repository.commitSha;

  function downloadAssessment() {
    downloadJson(assessment, `${assessment.repository.name}-assessment.json`);
  }

  function printAssessment() {
    window.print();
  }

  return (
    <div className="results-enter">
      <section className="repository-heading" aria-labelledby="repository-name">
        <div>
          <p className="eyebrow">Migration workspace</p>
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
          <Menu>
            <MenuTrigger disableButtonEnhancement>
              <Button appearance="secondary" icon={<ArrowDownload20Regular />}>Export</Button>
            </MenuTrigger>
            <MenuPopover>
              <MenuList>
                <MenuItem onClick={downloadAssessment}>Assessment JSON</MenuItem>
                {planning ? (
                  <>
                    <MenuItem onClick={() => downloadJson(planning.plan, `${assessment.repository.name}-migration-plan.json`)}>
                      Migration plan JSON
                    </MenuItem>
                    <MenuItem onClick={() => downloadReport(planning.runId, 'md')}>Markdown report</MenuItem>
                    <MenuItem onClick={() => downloadReport(planning.runId, 'html')}>HTML report</MenuItem>
                  </>
                ) : null}
              </MenuList>
            </MenuPopover>
          </Menu>
        </div>
      </section>

      <div className="repository-meta" aria-label="Repository metadata">
        <span><strong>Branch</strong> {assessment.repository.defaultBranch}</span>
        <span title={commitSha}><strong>Commit</strong> {commitSha.slice(0, 12)}</span>
        <span><strong>License</strong> {assessment.repository.license ?? 'Not detected'}</span>
        <span><strong>Generated</strong> {formatDate(assessment.generatedAt)}</span>
      </div>

      <div className="metric-grid" aria-label="Assessment summary">
        <div className="metric metric-dependencies" role="group" aria-label={pluralize(assessment.dependencies.length, 'dependency', 'dependencies')}>
          <span className="metric-value">{assessment.dependencies.length}</span>
          <span className="metric-label">
            {assessment.dependencies.length === 1 ? 'dependency' : 'dependencies'}
          </span>
        </div>
        <div className="metric metric-code" role="group" aria-label={pluralize(assessment.codeFindings.length, 'code finding')}>
          <span className="metric-value">{assessment.codeFindings.length}</span>
          <span className="metric-label">
            {assessment.codeFindings.length === 1 ? 'code finding' : 'code findings'}
          </span>
        </div>
        <div className="metric metric-files">
          <span className="metric-value">
            {assessment.scanCoverage.filesScanned}/{assessment.scanCoverage.filesTotal}
          </span>
          <span className="metric-label">files scanned</span>
        </div>
        <div className="metric metric-unknowns" role="group" aria-label={pluralize(assessment.unknowns.length, 'open unknown')}>
          <span className="metric-value">{assessment.unknowns.length}</span>
          <span className="metric-label">
            {assessment.unknowns.length === 1 ? 'open unknown' : 'open unknowns'}
          </span>
        </div>
      </div>

      <div className="assessment-layout">
        <aside className="section-rail" aria-label="Assessment sections">
          <a href="#migration-plan">Migration plan</a>
          {planning ? <a href="#migration-actions">Reviewable changes</a> : null}
          <a href="#coverage">Coverage</a>
          <a href="#technology">Technology</a>
          <a href="#dependencies">Dependencies</a>
          <a href="#code-findings">Code findings</a>
          <a href="#platform-signals">Platform signals</a>
          <a href="#unknowns">Unknowns</a>
        </aside>

        <div className="assessment-content">
          <PlanningResults planning={planning} pending={planningPending} error={planningError} />
          {planning ? (
            <TransformValidateResults
              planning={planning}
              target={{
                url: assessment.repository.url,
                commitSha: assessment.repository.commitSha,
              }}
              job={migrationJob}
              jobPending={migrationJobPending}
              jobError={migrationJobError}
              validationRun={validationRun}
              validationReport={validationReport}
              validationPending={validationPending}
              validationError={validationError}
              arm64Run={arm64Run}
              arm64Pending={arm64Pending}
              arm64Error={arm64Error}
              onRun={onRunMigration}
              onCancel={onCancelMigration}
            />
          ) : null}
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
  const [planning, setPlanning] = useState<MigrationPlanningResult | null>(null);
  const [planningError, setPlanningError] = useState<string | null>(null);
  const [progress, setProgress] = useState<AssessmentProgress>({
    phase: 'idle',
    percent: 0,
    message: 'Ready.',
  });
  const [error, setError] = useState<string | null>(null);
  const [inputError, setInputError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [authenticationMessage, setAuthenticationMessage] = useState<string | null>(null);
  const [migrationJob, setMigrationJob] = useState<MigrationJob | null>(null);
  const [migrationJobError, setMigrationJobError] = useState<string | null>(null);
  const [migrationJobPending, setMigrationJobPending] = useState(false);
  const [validationRun, setValidationRun] = useState<ValidationRunSummary | null>(null);
  const [validationError, setValidationError] = useState<string | null>(null);
  const [validationPending, setValidationPending] = useState(false);
  const [validationReport, setValidationReport] = useState<ValidationReport | null>(null);
  const [arm64Run, setArm64Run] = useState<Arm64RunStatus | null>(null);
  const [arm64Error, setArm64Error] = useState<string | null>(null);
  const [arm64Pending, setArm64Pending] = useState(false);
  const controllerRef = useRef<AbortController | null>(null);
  const migrationControllerRef = useRef<AbortController | null>(null);
  const authenticationSessionRef = useRef<string | null>(null);
  const assessmentJobRef = useRef<AssessmentJob | null>(null);
  const repositoryInputRef = useRef<HTMLInputElement>(null);

  function clearRepositoryUrl() {
    setSource('');
    setInputError(null);
    setError(null);
    repositoryInputRef.current?.focus();
  }

  function updateAssessmentProgress(nextProgress: AssessmentProgress) {
    setProgress(nextProgress);
  }

  async function runAssessment(
    normalizedSource: string,
    signal: AbortSignal,
    authenticationSessionId?: string,
  ) {
    try {
      return await assessRepositoryWithProgress(
        normalizedSource,
        updateAssessmentProgress,
        signal,
        authenticationSessionId,
        (job) => { assessmentJobRef.current = job; },
      );
    } finally {
      assessmentJobRef.current = null;
    }
  }

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

      return await runAssessment(normalizedSource, signal, session.sessionId);
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

    if (!isGitHubRepositoryUrl(normalizedSource)) {
      setInputError('Use an HTTPS GitHub URL in the form https://github.com/owner/repository.');
      return;
    }

    setInputError(null);
    setError(null);
    setAssessment(null);
    setPlanning(null);
    setPlanningError(null);
    setAuthenticationMessage(null);
    migrationControllerRef.current?.abort();
    migrationControllerRef.current = null;
    setMigrationJob(null);
    setMigrationJobError(null);
    setMigrationJobPending(false);
    setValidationRun(null);
    setValidationError(null);
    setValidationPending(false);
    setValidationReport(null);
    setProgress({ phase: 'queued', percent: 0, message: 'Submitting assessment.' });
    setLoading(true);
    const nextController = new AbortController();
    controllerRef.current = nextController;

    try {
      let result: RepositoryAssessment;
      try {
        result = await runAssessment(normalizedSource, nextController.signal);
      } catch (requestError) {
        if (!(requestError instanceof AssessmentApiError && requestError.authenticationRequired)) {
          throw requestError;
        }

        result = await authenticateAndRetry(normalizedSource, nextController.signal);
      }

      setAuthenticationMessage(null);
      setAssessment(result);
      setProgress({ phase: 'migration-planning', percent: 0, message: 'Scoring readiness and building the migration plan.' });
      try {
        const plan = await planMigration(result, nextController.signal);
        setPlanning(plan);
        setProgress({ phase: 'completed', percent: 100, message: 'Assessment and migration plan completed.' });
      } catch (requestError) {
        if (requestError instanceof DOMException && requestError.name === 'AbortError') throw requestError;
        setPlanningError(requestError instanceof Error ? requestError.message : 'Migration planning failed.');
      }
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
    const assessmentJob = assessmentJobRef.current;
    assessmentJobRef.current = null;
    if (assessmentJob) {
      void cancelAssessmentJob(assessmentJob).catch(() => undefined);
    }
    if (authenticationSessionId) {
      void cancelGitHubAuthentication(authenticationSessionId).catch(() => undefined);
    }
  }

  function cancelMigration() {
    migrationControllerRef.current?.abort();
    migrationControllerRef.current = null;
    setMigrationJobPending(false);
    setValidationPending(false);
  }

  async function handleRunMigration() {
    if (!planning || !assessment) return;
    migrationControllerRef.current?.abort();
    const controller = new AbortController();
    migrationControllerRef.current = controller;
    setMigrationJob(null);
    setMigrationJobError(null);
    setMigrationJobPending(true);
    setValidationRun(null);
    setValidationError(null);
    setValidationReport(null);
    setValidationPending(false);
    setArm64Run(null);
    setArm64Error(null);
    setArm64Pending(false);

    const target = {
      url: assessment.repository.url,
      commitSha: assessment.repository.commitSha,
    };

    try {
      const accepted = await submitMigrationJob(planning.plan, target, controller.signal);
      const finalJob = await pollMigrationJob(accepted.jobId, {
        onUpdate: (next) => setMigrationJob(next),
        signal: controller.signal,
      });
      setMigrationJob(finalJob);
      setMigrationJobPending(false);

      const dispatch = finalJob.result?.validation ?? null;
      const arm64Dispatch = finalJob.result?.arm64Build ?? null;

      const arm64Task = arm64Dispatch?.dispatched && arm64Dispatch.jobId
        ? (async (jobId: string) => {
            setArm64Pending(true);
            try {
              const finalArm64 = await pollArm64Run(jobId, {
                onUpdate: (next) => setArm64Run(next),
                signal: controller.signal,
              });
              setArm64Run(finalArm64);
            } catch (arm64ErrorRaised) {
              if (arm64ErrorRaised instanceof DOMException && arm64ErrorRaised.name === 'AbortError') return;
              setArm64Error(
                arm64ErrorRaised instanceof Error
                  ? arm64ErrorRaised.message
                  : 'ARM64 run status is unavailable.',
              );
            } finally {
              setArm64Pending(false);
            }
          })(arm64Dispatch.jobId)
        : Promise.resolve();

      if (finalJob.status === 'completed' && dispatch?.dispatched && dispatch.runId) {
        const runId = dispatch.runId;
        setValidationPending(true);
        try {
          const finalRun = await pollValidationRun(runId, {
            onUpdate: (next) => setValidationRun(next),
            signal: controller.signal,
          });
          setValidationRun(finalRun);
          if (finalRun.status === 'completed' || finalRun.status === 'failed') {
            try {
              const report = await getValidationReport(runId, controller.signal);
              setValidationReport(report);
            } catch (reportError) {
              if (reportError instanceof DOMException && reportError.name === 'AbortError') return;
              setValidationError(
                reportError instanceof Error ? reportError.message : 'Validation report is unavailable.',
              );
            }
          }
        } catch (runError) {
          if (runError instanceof DOMException && runError.name === 'AbortError') return;
          setValidationError(
            runError instanceof Error ? runError.message : 'Validation run status is unavailable.',
          );
        } finally {
          setValidationPending(false);
        }
      }

      await arm64Task;
    } catch (requestError) {
      if (requestError instanceof DOMException && requestError.name === 'AbortError') return;
      setMigrationJobError(
        requestError instanceof Error ? requestError.message : 'Migration actions failed.',
      );
    } finally {
      setMigrationJobPending(false);
      if (migrationControllerRef.current === controller) {
        migrationControllerRef.current = null;
      }
    }
  }

  return (
    <FluentProvider theme={webLightTheme} className="app-provider">
      <a className="skip-link" href="#top">Skip to content</a>
      <header className="global-header">
        <div className="global-header-inner">
          <a className="brand" href="#top" aria-label="Arm Migration Assist home">
            <span className="brand-mark" aria-hidden="true">
              <svg viewBox="0 0 36 36" role="presentation">
                <path d="M8 27 18 7l10 20h-6l-4-9-4 9H8Z" />
                <path className="brand-cut" d="M15.8 24h4.4L18 19.2 15.8 24Z" />
              </svg>
            </span>
            <span className="brand-copy">
              <strong>Arm Migration Assist</strong>
              <small>Migration engineering workspace</small>
            </span>
          </a>
          <div className="header-assurances" aria-label="Product safeguards">
            <span>No source execution</span>
            <span>Human approval required</span>
          </div>
        </div>
      </header>

      <main id="top">
        <section className="intake-band" aria-labelledby="page-title">
          <div className="intake-grid">
            <div className="page-intro">
              <p className="eyebrow">Windows on Arm</p>
              <h1 id="page-title">Plan your app for Windows on Arm</h1>
              <p className="page-context">
                Assess a GitHub repository and get an evidence-linked migration plan.
              </p>
              <div className="trust-row" aria-label="Analysis guarantees">
                <span><Checkmark16Regular aria-hidden="true" /> Commit-pinned</span>
                <span><Checkmark16Regular aria-hidden="true" /> Read-only</span>
                <span><Checkmark16Regular aria-hidden="true" /> Evidence-linked</span>
              </div>
            </div>

            <div className="intake-workbench">
              <div className="intake-heading">
                <strong>Start with a repository</strong>
                <p>Public or protected with local GitHub sign-in.</p>
              </div>
              <form className="assessment-form" onSubmit={handleSubmit} noValidate>
                <Field
                  className="repository-field"
                  label="GitHub repository URL"
                  validationMessage={inputError}
                  validationState={inputError ? 'error' : 'none'}
                >
                  <Input
                    ref={repositoryInputRef}
                    value={source}
                    onChange={(_, data) => {
                      setSource(data.value);
                      if (inputError) setInputError(null);
                    }}
                    contentBefore={<Search20Regular />}
                    contentAfter={source ? (
                      <Button
                        appearance="transparent"
                        aria-label="Clear repository URL"
                        disabled={loading}
                        icon={<Dismiss20Regular />}
                        size="small"
                        title="Clear repository URL"
                        type="button"
                        onClick={clearRepositoryUrl}
                      />
                    ) : undefined}
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
                    {loading
                      ? progress.phase === 'migration-planning' ? 'Planning migration' : 'Assessing repository'
                      : 'Run migration analysis'}
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
                <div className="pipeline-status" role="status" aria-live="polite">
                  <div>
                    <strong>{authenticationMessage ? 'Waiting for GitHub sign-in' : titleCase(progress.phase)}</strong>
                    <span>{progress.phase === 'migration-planning' ? 'Planning' : `${progress.percent}%`}</span>
                  </div>
                  <ProgressBar value={progress.phase === 'migration-planning' ? undefined : progress.percent / 100} />
                  <p>{authenticationMessage ?? progress.message}</p>
                </div>
              ) : null}
            </div>
          </div>
        </section>

        <ProductWorkflow
          assessment={assessment}
          planning={planning}
          planningError={planningError}
          loading={loading}
          progressPhase={progress.phase}
          migrationJob={migrationJob}
          migrationJobPending={migrationJobPending}
          migrationJobError={migrationJobError}
          validationRun={validationRun}
          validationReport={validationReport}
          validationPending={validationPending}
        />

        <div className="workspace">
          {assessment ? (
            <AssessmentResults
              assessment={assessment}
              planning={planning}
              planningPending={loading && progress.phase === 'migration-planning'}
              planningError={planningError}
              migrationJob={migrationJob}
              migrationJobPending={migrationJobPending}
              migrationJobError={migrationJobError}
              validationRun={validationRun}
              validationReport={validationReport}
              validationPending={validationPending}
              validationError={validationError}
              arm64Run={arm64Run}
              arm64Pending={arm64Pending}
              arm64Error={arm64Error}
              onRunMigration={handleRunMigration}
              onCancelMigration={cancelMigration}
            />
          ) : !loading ? (
            <section className="empty-workspace" aria-labelledby="empty-title">
              <div className="empty-intro">
                <p className="eyebrow">One controlled workflow</p>
                <h2 id="empty-title">From evidence to execution.</h2>
                <p>Analysis is read-only. Every proposed change remains reviewable and approval-gated.</p>
              </div>
              <div className="outcome-grid">
                <div><span>Assess</span><strong>Compatibility evidence</strong><p>Dependencies, native code, build, CI, and Windows signals.</p></div>
                <div><span>Plan</span><strong>Auditable strategy</strong><p>Readiness score, recommendation, risks, and acceptance criteria.</p></div>
                <div><span>Transform + validate</span><strong>Reviewable execution</strong><p>Approval-gated work, patches, and ARM64 validation.</p></div>
              </div>
            </section>
          ) : null}
        </div>
      </main>

      <footer>
        <span>Arm Migration Assist</span>
        <span>Evidence → strategy → reviewable change → validation</span>
      </footer>
    </FluentProvider>
  );
}