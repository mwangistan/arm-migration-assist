import type {
  MigrationPlanningResult,
  MigrationWorkItem,
  ReadinessDimension,
  RepositoryAssessment,
} from './types';

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function isStringArray(value: unknown): value is string[] {
  return Array.isArray(value) && value.every((item) => typeof item === 'string');
}

function isOptionalString(value: unknown) {
  return value === undefined || value === null || typeof value === 'string';
}

function isOptionalNumber(value: unknown) {
  return value === undefined || value === null || typeof value === 'number';
}

function isEvidence(value: unknown) {
  if (!isRecord(value)
      || typeof value.sourceType !== 'string'
      || typeof value.observation !== 'string'
      || !isOptionalString(value.path)
      || !isOptionalString(value.artifact)) {
    return false;
  }

  return (typeof value.path === 'string') !== (typeof value.artifact === 'string');
}

function isEvidenceArray(value: unknown) {
  return Array.isArray(value) && value.every(isEvidence);
}

function isProducer(value: unknown) {
  return isRecord(value)
    && typeof value.name === 'string'
    && typeof value.version === 'string'
    && isOptionalString(value.ruleset)
    && (value.scannerVersions === undefined
      || (Array.isArray(value.scannerVersions)
        && value.scannerVersions.every((scanner) => isRecord(scanner)
          && typeof scanner.name === 'string'
          && typeof scanner.version === 'string')));
}

function isRepository(value: unknown) {
  return isRecord(value)
    && typeof value.name === 'string'
    && typeof value.url === 'string'
    && typeof value.commitSha === 'string'
    && typeof value.defaultBranch === 'string'
    && isOptionalString(value.license);
}

function isTechnology(value: unknown) {
  return isRecord(value)
    && isStringArray(value.languages)
    && isStringArray(value.frameworks)
    && isStringArray(value.projectTypes)
    && isStringArray(value.buildSystems)
    && isStringArray(value.packageManagers)
    && isStringArray(value.installers)
    && isStringArray(value.ciSystems);
}

function isDependency(value: unknown) {
  return isRecord(value)
    && typeof value.evidenceId === 'string'
    && typeof value.name === 'string'
    && isOptionalString(value.version)
    && typeof value.ecosystem === 'string'
    && typeof value.type === 'string'
    && typeof value.criticality === 'string'
    && typeof value.architectureStatus === 'string'
    && isStringArray(value.availableArchitectures)
    && isStringArray(value.replacementCandidates)
    && isEvidenceArray(value.evidence)
    && typeof value.confidence === 'number';
}

function isCodeFinding(value: unknown) {
  return isRecord(value)
    && typeof value.evidenceId === 'string'
    && typeof value.ruleId === 'string'
    && typeof value.category === 'string'
    && typeof value.severity === 'string'
    && typeof value.file === 'string'
    && isOptionalNumber(value.line)
    && isOptionalNumber(value.column)
    && typeof value.description === 'string'
    && isEvidenceArray(value.evidence)
    && typeof value.confidence === 'number';
}

function isBuildFindings(value: unknown) {
  return isRecord(value)
    && typeof value.evidenceId === 'string'
    && typeof value.arm64TargetExists === 'boolean'
    && typeof value.arm64EcTargetExists === 'boolean'
    && typeof value.arm64CiJobExists === 'boolean'
    && typeof value.packagingSupportsArm64 === 'boolean'
    && typeof value.testsExist === 'boolean'
    && isStringArray(value.detectedTargets)
    && isEvidenceArray(value.evidence);
}

function isWindowsExperience(value: unknown) {
  return isRecord(value)
    && typeof value.windowsVersionExists === 'boolean'
    && typeof value.uiTechnology === 'string'
    && typeof value.installerExists === 'boolean'
    && typeof value.offlineCapable === 'boolean'
    && typeof value.accessibilityEvidence === 'string'
    && isOptionalString(value.accessibilityNotes)
    && typeof value.notificationsIntegrated === 'boolean'
    && typeof value.lifecycleIntegrated === 'boolean'
    && isEvidenceArray(value.evidence);
}

function isScanCoverage(value: unknown) {
  return isRecord(value)
    && typeof value.filesScanned === 'number'
    && typeof value.filesTotal === 'number'
    && typeof value.dependencyResolutionRate === 'number'
    && isStringArray(value.scannersCompleted)
    && isStringArray(value.scannersFailed);
}

function isUnknown(value: unknown) {
  return isRecord(value)
    && typeof value.description === 'string'
    && typeof value.area === 'string'
    && isOptionalString(value.requiredSkill)
    && (value.evidenceIds === undefined || isStringArray(value.evidenceIds));
}

function isAvailableSkill(value: unknown) {
  return isRecord(value)
    && typeof value.name === 'string'
    && typeof value.version === 'string'
    && typeof value.description === 'string'
    && typeof value.writeAccess === 'boolean'
    && isStringArray(value.supportedInputs)
    && isStringArray(value.supportedOutputs);
}

function isRepositoryAssessment(value: unknown): value is RepositoryAssessment {
  return isRecord(value)
    && typeof value.schemaVersion === 'string'
    && typeof value.assessmentId === 'string'
    && typeof value.generatedAt === 'string'
    && isProducer(value.producer)
    && isRepository(value.repository)
    && isTechnology(value.technology)
    && Array.isArray(value.dependencies)
    && value.dependencies.every(isDependency)
    && Array.isArray(value.codeFindings)
    && value.codeFindings.every(isCodeFinding)
    && isBuildFindings(value.buildFindings)
    && isWindowsExperience(value.windowsExperience)
    && isScanCoverage(value.scanCoverage)
    && Array.isArray(value.unknowns)
    && value.unknowns.every(isUnknown)
    && Array.isArray(value.availableSkills)
    && value.availableSkills.every(isAvailableSkill);
}

async function readJson(response: Response): Promise<unknown> {
  return response.json().catch(() => null) as Promise<unknown>;
}

function assessmentApiUrl(path: string) {
  const configuredBase = import.meta.env.VITE_ASSESSMENT_API_URL?.trim();
  return configuredBase ? `${configuredBase.replace(/\/+$/, '')}${path}` : path;
}

function migrationPlannerApiUrl(path: string) {
  const configuredBase = import.meta.env.VITE_MIGRATION_PLANNER_API_URL?.trim();
  return configuredBase ? `${configuredBase.replace(/\/+$/, '')}${path}` : path;
}

function problemMessage(value: unknown) {
  if (!isRecord(value)) {
    return 'Assessment failed.';
  }

  if (isRecord(value.errors)
      && Array.isArray(value.errors.source)
      && typeof value.errors.source[0] === 'string') {
    return value.errors.source[0];
  }

  if (typeof value.detail === 'string') {
    return value.detail;
  }

  return typeof value.title === 'string' ? value.title : 'Assessment failed.';
}

export class AssessmentApiError extends Error {
  constructor(
    message: string,
    readonly status: number,
    readonly authenticationRequired: boolean,
  ) {
    super(message);
    this.name = 'AssessmentApiError';
  }
}

export interface GitHubAuthenticationSession {
  sessionId: string;
  status: 'pending' | 'succeeded' | 'failed';
  expiresAt: string;
  message: string | null;
}

export interface AssessmentProgress {
  phase: string;
  percent: number;
  message: string;
}

export interface AssessmentJob extends AssessmentProgress {
  jobId: string;
  status: 'queued' | 'running' | 'completed' | 'failed' | 'canceled';
  errorCode: string | null;
  error: string | null;
  statusUrl: string;
  eventsUrl: string;
  resultUrl: string;
}

function isAuthenticationSession(value: unknown): value is GitHubAuthenticationSession {
  return isRecord(value)
    && typeof value.sessionId === 'string'
    && (value.status === 'pending' || value.status === 'succeeded' || value.status === 'failed')
    && typeof value.expiresAt === 'string'
    && isOptionalString(value.message);
}

function hasAuthenticationRequirement(value: unknown) {
  return isRecord(value) && value.authenticationRequired === true;
}

function isAssessmentJob(value: unknown): value is AssessmentJob {
  return isRecord(value)
    && typeof value.jobId === 'string'
    && ['queued', 'running', 'completed', 'failed', 'canceled'].includes(String(value.status))
    && typeof value.phase === 'string'
    && typeof value.percent === 'number'
    && typeof value.message === 'string'
    && isOptionalString(value.errorCode)
    && isOptionalString(value.error)
    && typeof value.statusUrl === 'string'
    && typeof value.eventsUrl === 'string'
    && typeof value.resultUrl === 'string';
}

function isAssessmentProgress(value: unknown): value is AssessmentProgress & { eventType: string } {
  return isRecord(value)
    && typeof value.eventType === 'string'
    && typeof value.phase === 'string'
    && typeof value.percent === 'number'
    && typeof value.message === 'string';
}

function isReadinessDimension(value: unknown): value is ReadinessDimension {
  return isRecord(value)
    && typeof value.dimensionKey === 'string'
    && typeof value.weightPct === 'number'
    && typeof value.rawScore === 'number'
    && typeof value.weightedContribution === 'number'
    && typeof value.confidence === 'number'
    && isStringArray(value.rationaleCodes)
    && isStringArray(value.evidenceIds);
}

function isMigrationWorkItem(value: unknown): value is MigrationWorkItem {
  return isRecord(value)
    && typeof value.id === 'string'
    && typeof value.sequence === 'number'
    && typeof value.priority === 'string'
    && typeof value.title === 'string'
    && typeof value.objective === 'string'
    && typeof value.agentOrSkill === 'string'
    && isStringArray(value.inputs)
    && isStringArray(value.expectedOutputs)
    && isStringArray(value.dependencies)
    && isStringArray(value.evidenceIds)
    && isStringArray(value.guidanceIds)
    && Array.isArray(value.acceptanceTests)
    && value.acceptanceTests.every((test) => isRecord(test)
      && typeof test.id === 'string'
      && typeof test.description === 'string'
      && typeof test.expectedOutcome === 'string')
    && typeof value.approvalRequired === 'boolean'
    && typeof value.estimatedEffort === 'string'
    && typeof value.risk === 'string';
}

function isMigrationPlanningResult(value: unknown): value is MigrationPlanningResult {
  if (!isRecord(value) || !isRecord(value.plan) || !isRecord(value.score)) return false;

  const plan = value.plan;
  const score = value.score;
  return typeof value.runId === 'string'
    && isStringArray(value.warnings)
    && typeof plan.schemaVersion === 'string'
    && typeof plan.planId === 'string'
    && typeof plan.assessmentId === 'string'
    && typeof plan.generatedAt === 'string'
    && isRecord(plan.modelProvenance)
    && typeof plan.modelProvenance.provider === 'string'
    && typeof plan.modelProvenance.name === 'string'
    && typeof plan.modelProvenance.version === 'string'
    && typeof plan.recommendedPath === 'string'
    && typeof plan.confidence === 'string'
    && typeof plan.executiveSummary === 'string'
    && typeof plan.scoreInterpretation === 'string'
    && Array.isArray(plan.workItems)
    && plan.workItems.every(isMigrationWorkItem)
    && Array.isArray(plan.alternatives)
    && Array.isArray(plan.risks)
    && Array.isArray(plan.unknowns)
    && Array.isArray(plan.missingSkills)
    && Array.isArray(plan.requiredApprovals)
    && isRecord(plan.validationPlan)
    && typeof score.schemaVersion === 'string'
    && typeof score.assessmentId === 'string'
    && typeof score.generatedAt === 'string'
    && typeof score.overallScore === 'number'
    && typeof score.uncappedScore === 'number'
    && typeof score.band === 'string'
    && typeof score.evidenceCompleteness === 'string'
    && typeof score.evidenceCompletenessScore === 'number'
    && typeof score.provisional === 'boolean'
    && isStringArray(score.provisionalReasons)
    && Array.isArray(score.dimensions)
    && score.dimensions.every(isReadinessDimension)
    && Array.isArray(score.capsApplied)
    && Array.isArray(score.majorBlockers)
    && typeof score.scoreSummary === 'string';
}

async function getAssessmentJob(path: string, signal?: AbortSignal): Promise<AssessmentJob> {
  const response = await fetch(assessmentApiUrl(path), { signal });
  const payload = await readJson(response);
  if (!response.ok || !isAssessmentJob(payload)) {
    throw new Error(response.ok
      ? 'The assessment service returned an invalid job status.'
      : problemMessage(payload));
  }

  return payload;
}

function waitForAssessmentPoll(signal?: AbortSignal) {
  return new Promise<void>((resolve, reject) => {
    if (signal?.aborted) {
      reject(new DOMException('The operation was aborted.', 'AbortError'));
      return;
    }

    const handleAbort = () => {
      window.clearTimeout(timeout);
      reject(new DOMException('The operation was aborted.', 'AbortError'));
    };
    const timeout = window.setTimeout(() => {
      signal?.removeEventListener('abort', handleAbort);
      resolve();
    }, 750);
    signal?.addEventListener('abort', handleAbort, { once: true });
  });
}

async function pollAssessmentJob(
  job: AssessmentJob,
  onProgress: (progress: AssessmentProgress) => void,
  signal?: AbortSignal,
): Promise<AssessmentJob> {
  let current = job;
  while (current.status === 'queued' || current.status === 'running') {
    await waitForAssessmentPoll(signal);
    current = await getAssessmentJob(job.statusUrl, signal);
    onProgress(current);
  }

  return current;
}

function streamAssessmentJob(
  job: AssessmentJob,
  onProgress: (progress: AssessmentProgress) => void,
  signal?: AbortSignal,
): Promise<AssessmentJob> {
  if (typeof EventSource === 'undefined') {
    return pollAssessmentJob(job, onProgress, signal);
  }

  return new Promise((resolve, reject) => {
    const stream = new EventSource(assessmentApiUrl(job.eventsUrl));
    let settled = false;

    const finish = (action: () => void) => {
      if (settled) return;
      settled = true;
      stream.close();
      signal?.removeEventListener('abort', handleAbort);
      action();
    };
    const handleAbort = () => finish(() => reject(
      new DOMException('The operation was aborted.', 'AbortError'),
    ));

    stream.addEventListener('assessment', (event) => {
      let payload: unknown;
      try {
        payload = JSON.parse((event as MessageEvent<string>).data);
      } catch {
        finish(() => reject(new Error('The assessment service returned an invalid progress event.')));
        return;
      }

      if (!isAssessmentProgress(payload)) {
        finish(() => reject(new Error('The assessment service returned an invalid progress event.')));
        return;
      }

      onProgress(payload);
      if (['completed', 'failed', 'canceled'].includes(payload.eventType)) {
        finish(() => {
          void getAssessmentJob(job.statusUrl, signal).then(resolve, reject);
        });
      }
    });
    stream.onerror = () => finish(() => {
      void pollAssessmentJob(job, onProgress, signal).then(resolve, reject);
    });
    signal?.addEventListener('abort', handleAbort, { once: true });
    if (signal?.aborted) handleAbort();
  });
}

export async function assessRepositoryWithProgress(
  source: string,
  onProgress: (progress: AssessmentProgress) => void,
  signal?: AbortSignal,
  authenticationSessionId?: string,
  onJobCreated?: (job: AssessmentJob) => void,
): Promise<RepositoryAssessment> {
  const response = await fetch(assessmentApiUrl('/api/assessment-jobs'), {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ source, authenticationSessionId }),
    signal,
  });
  const payload = await readJson(response);
  if (!response.ok || !isAssessmentJob(payload)) {
    throw new AssessmentApiError(
      response.ok ? 'The assessment service returned an invalid job.' : problemMessage(payload),
      response.status,
      hasAuthenticationRequirement(payload),
    );
  }

  onJobCreated?.(payload);
  onProgress(payload);
  const completed = await streamAssessmentJob(payload, onProgress, signal);
  if (completed.status !== 'completed') {
    throw new AssessmentApiError(
      completed.error ?? completed.message,
      completed.errorCode === 'authentication-required' ? 401 : 422,
      completed.errorCode === 'authentication-required',
    );
  }

  const resultResponse = await fetch(assessmentApiUrl(completed.resultUrl), { signal });
  const result = await readJson(resultResponse);
  if (!resultResponse.ok) {
    throw new AssessmentApiError(
      problemMessage(result),
      resultResponse.status,
      hasAuthenticationRequirement(result),
    );
  }
  if (!isRepositoryAssessment(result)) {
    throw new Error('The assessment service returned an invalid response.');
  }

  return result;
}

export async function cancelAssessmentJob(job: AssessmentJob): Promise<void> {
  const response = await fetch(assessmentApiUrl(job.statusUrl), { method: 'DELETE' });
  if (!response.ok && response.status !== 404 && response.status !== 409) {
    throw new Error('The assessment job could not be canceled.');
  }
}

export async function planMigration(
  assessment: RepositoryAssessment,
  signal?: AbortSignal,
): Promise<MigrationPlanningResult> {
  const response = await fetch(migrationPlannerApiUrl('/api/migration-plans'), {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(assessment),
    signal,
  });
  const payload = await readJson(response);
  if (!response.ok) {
    throw new Error(problemMessage(payload).replace(/^Assessment failed\.$/, 'Migration planning failed.'));
  }
  if (!isMigrationPlanningResult(payload)) {
    throw new Error('The migration planner returned an invalid response.');
  }
  if (payload.plan.assessmentId !== assessment.assessmentId
      || payload.score.assessmentId !== assessment.assessmentId) {
    throw new Error('The migration planner returned a result for a different assessment.');
  }

  return payload;
}

export function migrationReportUrl(runId: string, extension: 'md' | 'html') {
  return migrationPlannerApiUrl(
    `/api/migration-plans/${encodeURIComponent(runId)}/report.${extension}`,
  );
}

export async function assessRepository(
  source: string,
  signal?: AbortSignal,
  authenticationSessionId?: string,
): Promise<RepositoryAssessment> {
  const response = await fetch(assessmentApiUrl('/api/assessments'), {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ source, authenticationSessionId }),
    signal,
  });

  const payload = await readJson(response);
  if (!response.ok) {
    throw new AssessmentApiError(
      problemMessage(payload),
      response.status,
      hasAuthenticationRequirement(payload),
    );
  }

  if (!isRepositoryAssessment(payload)) {
    throw new Error('The assessment service returned an invalid response.');
  }

  return payload;
}

export async function startGitHubAuthentication(
  signal?: AbortSignal,
): Promise<GitHubAuthenticationSession> {
  const response = await fetch(assessmentApiUrl('/api/auth/github/sessions'), {
    method: 'POST',
    headers: { 'X-Arm-Migration-Client': 'dashboard' },
    signal,
  });
  const payload = await readJson(response);
  if (!response.ok || !isAuthenticationSession(payload)) {
    throw new Error(response.ok
      ? 'The authentication service returned an invalid response.'
      : problemMessage(payload));
  }

  return payload;
}

export async function getGitHubAuthentication(
  sessionId: string,
  signal?: AbortSignal,
): Promise<GitHubAuthenticationSession> {
  const response = await fetch(assessmentApiUrl(`/api/auth/github/sessions/${encodeURIComponent(sessionId)}`), {
    signal,
  });
  const payload = await readJson(response);
  if (!response.ok || !isAuthenticationSession(payload)) {
    throw new Error(response.status === 404
      ? 'The GitHub sign-in session expired.'
      : response.ok
        ? 'The authentication service returned an invalid response.'
        : problemMessage(payload));
  }

  return payload;
}

export async function cancelGitHubAuthentication(sessionId: string): Promise<void> {
  const response = await fetch(assessmentApiUrl(`/api/auth/github/sessions/${encodeURIComponent(sessionId)}`), {
    method: 'DELETE',
    headers: { 'X-Arm-Migration-Client': 'dashboard' },
  });
  if (!response.ok && response.status !== 404) {
    throw new Error('GitHub sign-in could not be canceled.');
  }
}