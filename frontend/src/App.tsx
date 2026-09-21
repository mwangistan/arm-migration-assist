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
  Arm64RunStatus,
  Arm64StepOutcome,
  CodeFinding,
  CriterionResult,
  CriterionResultStatus,
  DependencyFinding,
  GeneratedPatch,
  MigrationJob,
  MigrationPlanningResult,
  MigrationWorkItem,
  OverallScorecardStatus,
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
  arm64Run,
  arm64Pending,
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
  arm64Run: Arm64RunStatus | null;
  arm64Pending: boolean;
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
  const arm64Active = arm64Pending || arm64Run?.status === 'running' || arm64Run?.status === 'queued';
  const arm64Phase = arm64Run?.progress?.steps.find((s) => s.status === 'running')?.label ?? null;
  const validateDetail = scorecard
    ? scorecard.status === 'validated'
      ? `${scorecard.passed} passed`
      : scorecard.status === 'validation-failed'
        ? `${scorecard.failed} failed`
        : scorecard.status === 'partially-validated'
          ? `${scorecard.passed}/${scorecard.passed + scorecard.failed + scorecard.notRun + scorecard.inconclusive + scorecard.skipped} passed`
          : 'Not validated'
    : arm64Active
      ? arm64Phase ? `ARM64: ${arm64Phase}` : 'ARM64 build running'
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
      : validationPending || arm64Active
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

function arm64OverallMeta(run: Arm64RunStatus | null, pending: boolean): StatusMeta {
  if (run?.status === 'completed') {
    const build = run.scorecard?.build ?? null;
    const tests = run.scorecard?.tests ?? null;
    if (build && !build.succeeded) return { label: 'Build failed', color: 'danger' };
    if (tests && !tests.succeeded) return { label: 'Tests failed', color: 'warning' };
    return { label: 'Build passed', color: 'success' };
  }
  if (run?.status === 'failed') return { label: 'Failed', color: 'danger' };
  if (run?.status === 'cancelled') return { label: 'Cancelled', color: 'subtle' };
  if (pending || run?.status === 'running') return { label: 'Running', color: 'informative' };
  return { label: 'Queued', color: 'subtle' };
}

function arm64StepStatusMeta(status: string): StatusMeta {
  switch (status) {
    case 'succeeded': return { label: 'Done', color: 'success' };
    case 'running': return { label: 'Running', color: 'informative' };
    case 'failed': return { label: 'Failed', color: 'danger' };
    case 'skipped': return { label: 'Skipped', color: 'subtle' };
    default: return { label: 'Pending', color: 'subtle' };
  }
}

function arm64StepDuration(step: { startedAt: string | null; finishedAt: string | null }): string | null {
  if (!step.startedAt) return null;
  const end = step.finishedAt ? new Date(step.finishedAt).getTime() : Date.now();
  const seconds = Math.max(0, Math.round((end - new Date(step.startedAt).getTime()) / 1000));
  return `${seconds}s`;
}

function Arm64BuildPanel({
  dispatch,
  run,
  pending,
  error,
}: {
  dispatch: { dispatched: boolean; error: string | null } | null;
  run: Arm64RunStatus | null;
  pending: boolean;
  error: string | null;
}) {
  if (!dispatch && !run && !pending && !error) return null;

  const steps = run?.progress?.steps ?? [];
  const percent = run?.progress?.percent ?? 0;
  const sc = run?.scorecard ?? null;
  const active = pending || run?.status === 'running' || run?.status === 'queued';

  return (
    <div className="validation-panel arm64-panel" aria-labelledby="arm64-heading">
      <div className="section-heading">
        <div>
          <p className="eyebrow">ARM64 hardware build</p>
          <h4 id="arm64-heading">Runner on Azure Cobalt 100</h4>
        </div>
        <StatusBadge meta={arm64OverallMeta(run, pending)} />
      </div>

      <p className="transform-context">
        Clones the pinned commit on a real Arm64 VM, applies the generated patches, then runs a
        native build and test pass. This can take several minutes — the steps below update live.
      </p>

      {dispatch && !dispatch.dispatched ? (
        <MessageBar intent="warning">
          <MessageBarBody>
            ARM64 build was not dispatched.{dispatch.error ? ` ${dispatch.error}` : ''}
          </MessageBarBody>
        </MessageBar>
      ) : null}

      {active ? (
        <ProgressBar
          aria-label="ARM64 build in progress"
          value={percent > 0 ? percent / 100 : undefined}
        />
      ) : null}

      {error ? (
        <MessageBar intent="error"><MessageBarBody>{error}</MessageBarBody></MessageBar>
      ) : null}

      {run?.status === 'failed' && run.error ? (
        <MessageBar intent="error"><MessageBarBody>{run.error}</MessageBarBody></MessageBar>
      ) : null}

      {steps.length > 0 ? (
        <ol className="arm64-steps" aria-label="ARM64 build steps">
          {steps.map((step) => {
            const duration = arm64StepDuration(step);
            return (
              <li key={step.key} className={`arm64-step arm64-step-${step.status}`}>
                <span className="arm64-step-icon" aria-hidden="true">
                  {step.status === 'running' ? <Spinner size="tiny" />
                    : step.status === 'succeeded' ? <Checkmark16Regular />
                      : step.status === 'failed' ? <Dismiss20Regular />
                        : step.status === 'skipped' ? '—' : '•'}
                </span>
                <span className="arm64-step-body">
                  <strong>{step.label}</strong>
                  {step.detail ? <small>{step.detail}</small> : null}
                </span>
                <span className="arm64-step-meta">
                  <StatusBadge meta={arm64StepStatusMeta(step.status)} />
                  {duration ? <small>{duration}</small> : null}
                </span>
              </li>
            );
          })}
        </ol>
      ) : null}

      {sc ? (
        <div className="arm64-scorecard">
          <div className="scorecard-grid">
            <div><span className="metric-label">Build</span><span className="metric-value">{sc.build ? (sc.build.succeeded ? 'PASS' : 'FAIL') : 'n/a'}</span></div>
            <div><span className="metric-label">Tests</span><span className="metric-value">{sc.tests ? (sc.tests.succeeded ? 'PASS' : 'FAIL') : 'n/a'}</span></div>
            <div><span className="metric-label">Applied</span><span className="metric-value">{sc.patchApplication.applied.length}</span></div>
            <div><span className="metric-label">Wall clock</span><span className="metric-value">{Math.round(sc.wallClockSeconds)}s</span></div>
          </div>
          <p className="arm64-hardware">
            {sc.hardware.cpuModel} · {sc.hardware.cpuCount} vCPU · {sc.hardware.architecture} · {sc.hardware.vmSku} ({sc.hardware.region})
          </p>
          {sc.build && !sc.build.succeeded && sc.build.stderrTail ? (
            <pre className="patch-diff" aria-label="ARM64 build error output">{sc.build.stderrTail}</pre>
          ) : null}
        </div>
      ) : null}
    </div>
  );
}

// The deterministic F4 validation run cannot execute a build for interpreted
// stacks (e.g. Python), so it returns every criterion as not-run with "no
// executable mapping or measured evidence". The real measured evidence for those
// generic ARM64 build/functional criteria is produced by the ARM64 runner on the
// Cobalt VM. Once the runner reaches a terminal state, fold its measured build /
// test outcomes into the two generic validation-sourced criteria. Per-package
// acceptance criteria are intentionally left untouched — one runner build does
// not prove each declared package builds.
// A criterion is "machine-measured" when the ARM64 runner (or F4 for compiled
// stacks) can produce executable evidence for it. Those are the generic
// `validation:` build/functional checks. Everything else (`acceptance:` per
// work-item tests, `discovered:` device targets) is a manual sign-off item.
export function isMeasuredCriterion(result: CriterionResult): boolean {
  return result.criterion.key.startsWith('validation:');
}

export function foldArm64Evidence(report: ValidationReport, run: Arm64RunStatus | null): ValidationReport {
  const card = run?.scorecard ?? null;
  if (!run || (run.status !== 'completed' && run.status !== 'failed') || !card) {
    return report;
  }

  const runnerLabel = `${card.hardware.vmSku} (${card.hardware.architecture})`;
  const applyOutcome = (
    result: CriterionResult,
    outcome: Arm64StepOutcome | null,
    kind: 'build' | 'test',
  ): CriterionResult => {
    if (!outcome || result.status !== 'not-run') {
      return result;
    }
    const verdict = outcome.succeeded ? 'succeeded' : `failed (exit ${outcome.exitCode})`;
    return {
      ...result,
      status: outcome.succeeded ? 'passed' : 'failed',
      reason: ` Measured on the ARM64 runner: ${kind} ${verdict} on ${runnerLabel} via \`${outcome.command}\`.`,
      evidenceIds: [...result.evidenceIds, `arm64-run:${run.jobId}`],
    };
  };

  const criteria = report.scorecard.criteria.map((result) => {
    const { key, category } = result.criterion;
    if (!key.startsWith('validation:')) {
      return result;
    }
    if (category === 'build') {
      return applyOutcome(result, card.build, 'build');
    }
    if (category === 'functional') {
      return applyOutcome(result, card.tests, 'test');
    }
    return result;
  });

  // Headline counts and overall status reflect only the machine-measured criteria
  // (the generic `validation:` build + functional checks the ARM64 runner actually
  // executes). The per-work-item `acceptance:` and `discovered:` device criteria are
  // manual sign-off items the automated pipeline can never mark passed, so counting
  // them here would permanently drag a genuinely successful ARM64 run down to
  // "partially-validated" with a wall of not-run. They remain in `criteria` and are
  // surfaced separately in the UI as manual criteria.
  const measured = criteria.filter((c) => isMeasuredCriterion(c));
  const count = (status: CriterionResultStatus) => measured.filter((c) => c.status === status).length;
  const passed = count('passed');
  const failed = count('failed');
  const notRun = count('not-run');
  const inconclusive = count('inconclusive');
  const skipped = count('skipped');
  const status: OverallScorecardStatus =
    failed > 0
      ? 'validation-failed'
      : measured.length > 0 && passed === measured.length
        ? 'validated'
        : passed > 0
          ? 'partially-validated'
          : 'not-validated';

  return {
    ...report,
    scorecard: { ...report.scorecard, status, passed, failed, notRun, inconclusive, skipped, criteria },
  };
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
  const arm64Active = arm64Pending || arm64Run?.status === 'running' || arm64Run?.status === 'queued';
  const arm64Dispatched = Boolean(arm64Dispatch?.dispatched);
  // When the ARM64 runner is the source of measured evidence, hold the scorecard
  // until the runner reaches a terminal state, then present the merged result.
  const awaitingArm64Evidence = arm64Dispatched && arm64Active;
  const effectiveReport = validationReport && arm64Dispatched
    ? foldArm64Evidence(validationReport, arm64Run)
    : validationReport;
  const scorecard = effectiveReport?.scorecard ?? null;
  const measuredCriteria = scorecard?.criteria.filter(isMeasuredCriterion) ?? [];
  const manualCriteria = scorecard?.criteria.filter((c) => !isMeasuredCriterion(c)) ?? [];
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

      <Arm64BuildPanel
        dispatch={arm64Dispatch}
        run={arm64Run}
        pending={arm64Pending}
        error={arm64Error}
      />

      {dispatch ? (
        <div className="validation-panel" aria-labelledby="validation-heading">
          <div className="section-heading">
            <div>
              <p className="eyebrow">ARM64 validation</p>
              <h4 id="validation-heading">Validation run</h4>
            </div>
            <StatusBadge meta={
              awaitingArm64Evidence
                ? { label: 'Measuring on ARM64 runner', color: 'informative' }
                : scorecard
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

          {awaitingArm64Evidence ? (
            <>
              <ProgressBar aria-label="Waiting for ARM64 build and test evidence" />
              <MessageBar intent="info">
                <MessageBarBody>
                  Waiting for measured build + test evidence from the ARM64 runner before
                  scoring. The build and functional criteria resolve once the runner above
                  finishes — this can take several minutes.
                </MessageBarBody>
              </MessageBar>
            </>
          ) : null}

          {validationError ? (
            <MessageBar intent="error"><MessageBarBody>{validationError}</MessageBarBody></MessageBar>
          ) : null}

          {validationRun?.status === 'failed' && validationRun.error ? (
            <MessageBar intent="error"><MessageBarBody>{validationRun.error}</MessageBarBody></MessageBar>
          ) : null}

          {scorecard && !awaitingArm64Evidence ? (
            <>
              <div className="scorecard-grid">
                <div><span className="metric-label">Passed</span><span className="metric-value">{scorecard.passed}</span></div>
                <div><span className="metric-label">Failed</span><span className="metric-value">{scorecard.failed}</span></div>
                <div><span className="metric-label">Inconclusive</span><span className="metric-value">{scorecard.inconclusive}</span></div>
                <div><span className="metric-label">Not run</span><span className="metric-value">{scorecard.notRun}</span></div>
                <div><span className="metric-label">Skipped</span><span className="metric-value">{scorecard.skipped}</span></div>
              </div>
              <p className="scorecard-caption">
                Counts above cover the machine-measured ARM64 build &amp; functional checks.
                Per-work-item acceptance criteria and device targets require manual sign-off
                and are listed separately below.
              </p>

              <div className="criteria-list" aria-label="Measured validation criteria">
                {measuredCriteria.length > 0 ? (
                  measuredCriteria.map((criterion) => (
                    <div className="criteria-row" key={criterion.criterion.key}>
                      <div>
                        <Badge appearance="outline" color="informative" size="small">
                          {titleCase(criterion.criterion.category)}
                        </Badge>
                        <StatusBadge meta={criterionStatusMeta(criterion.status)} />
                      </div>
                      <p><strong>{criterion.criterion.description}</strong>{criterion.reason}</p>
                    </div>
                  ))
                ) : (
                  <p className="scorecard-caption">No machine-measurable ARM64 criteria were defined for this plan.</p>
                )}
              </div>

              {manualCriteria.length > 0 ? (
                <details className="manual-criteria">
                  <summary>
                    Manual acceptance criteria ({manualCriteria.length}) — require human sign-off, not auto-run
                  </summary>
                  <div className="criteria-list" aria-label="Manual acceptance criteria">
                    {manualCriteria.map((criterion) => (
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

                  {effectiveReport && effectiveReport.coverageGaps.length > 0 ? (
                    <div className="coverage-gaps" aria-label="Coverage gaps">
                      <h5>Coverage gaps</h5>
                      <ul>
                        {effectiveReport.coverageGaps.map((gap) => (
                          <li key={gap.id}>
                            <strong>{gap.description}</strong>
                            <span>{gap.criterionKeys.join(', ')}</span>
                          </li>
                        ))}
                      </ul>
                    </div>
                  ) : null}
                </details>
              ) : null}
            </>
          ) : null}
        </div>
      ) : null}
    </section>
  );
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
  const [arm64Pending, setArm64Pending] = useState(false);
  const [arm64Error, setArm64Error] = useState<string | null>(null);
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
    setArm64Run(null);
    setArm64Error(null);
    setArm64Pending(false);
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
    setArm64Pending(false);
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

      const validationTask = (async () => {
        if (finalJob.status !== 'completed' || !dispatch?.dispatched || !dispatch.runId) return;
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
      })();

      const arm64Task = (async () => {
        if (finalJob.status !== 'completed' || !arm64Dispatch?.dispatched || !arm64Dispatch.jobId) return;
        setArm64Pending(true);
        try {
          const finalRun = await pollArm64Run(arm64Dispatch.jobId, {
            onUpdate: (next) => setArm64Run(next),
            signal: controller.signal,
          });
          setArm64Run(finalRun);
        } catch (runError) {
          if (runError instanceof DOMException && runError.name === 'AbortError') return;
          setArm64Error(
            runError instanceof Error ? runError.message : 'ARM64 build status is unavailable.',
          );
        } finally {
          setArm64Pending(false);
        }
      })();

      await Promise.all([validationTask, arm64Task]);
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
          arm64Run={arm64Run}
          arm64Pending={arm64Pending}
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