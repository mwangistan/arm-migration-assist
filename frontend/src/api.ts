import type {
  Arm64BuildDispatch,
  Arm64RunStatus,
  BranchApplication,
  CriterionResult,
  CriterionResultStatus,
  GeneratedPatch,
  MigrationActionsResult,
  MigrationJob,
  MigrationJobAccepted,
  MigrationJobStatus,
  MigrationPlanningResult,
  MigrationWorkItem,
  OverallScorecardStatus,
  PatchRejection,
  ReadinessDimension,
  RepositoryAssessment,
  SkippedWorkItem,
  ValidationCoverageGap,
  ValidationDispatch,
  ValidationReport,
  ValidationRunStatus,
  ValidationRunSummary,
  ValidationScorecard,
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

function automationApiUrl(path: string) {
  const configuredBase = import.meta.env.VITE_AUTOMATION_API_URL?.trim();
  return configuredBase ? `${configuredBase.replace(/\/+$/, '')}${path}` : path;
}

function validationApiUrl(path: string) {
  const configuredBase = import.meta.env.VITE_VALIDATION_API_URL?.trim();
  return configuredBase ? `${configuredBase.replace(/\/+$/, '')}${path}` : path;
}

async function fetchService(
  input: string,
  init: RequestInit | undefined,
  unavailableMessage: string,
) {
  try {
    return await fetch(input, init);
  } catch (error) {
    if (error instanceof Error && error.name === 'AbortError') throw error;
    throw new Error(unavailableMessage, { cause: error });
  }
}

function fetchAssessmentService(path: string, init?: RequestInit) {
  return fetchService(
    assessmentApiUrl(path),
    init,
    'The assessment service is unavailable. Check your connection and try again.',
  );
}

function fetchMigrationPlanner(path: string, init?: RequestInit) {
  return fetchService(
    migrationPlannerApiUrl(path),
    init,
    'The migration planner is unavailable. Your assessment results are still available.',
  );
}

function fetchAutomationService(path: string, init?: RequestInit) {
  return fetchService(
    automationApiUrl(path),
    init,
    'The migration action runner is unavailable. Your plan is still available.',
  );
}

function fetchValidationService(path: string, init?: RequestInit) {
  return fetchService(
    validationApiUrl(path),
    init,
    'The validation service is unavailable. Migration patches are still available.',
  );
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
  const response = await fetchAssessmentService(path, { signal });
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
  const response = await fetchAssessmentService('/api/assessment-jobs', {
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

  const resultResponse = await fetchAssessmentService(completed.resultUrl, { signal });
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
  const response = await fetchAssessmentService(job.statusUrl, { method: 'DELETE' });
  if (!response.ok && response.status !== 404 && response.status !== 409) {
    throw new Error('The assessment job could not be canceled.');
  }
}

export async function planMigration(
  assessment: RepositoryAssessment,
  signal?: AbortSignal,
): Promise<MigrationPlanningResult> {
  const response = await fetchMigrationPlanner('/api/migration-plans', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(assessment),
    signal,
  });
  // 202 Accepted → async run; poll the returned statusUrl until it terminates.
  // 200 OK is not used by the current server but kept as a fallback for older builds.
  if (response.status === 202) {
    const acceptEnvelope = await readJson(response);
    if (!isRecord(acceptEnvelope) || typeof acceptEnvelope.runId !== 'string') {
      throw new Error('The migration planner returned an invalid acceptance envelope.');
    }
    return await pollMigrationPlanRun(acceptEnvelope.runId, assessment, signal);
  }

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

async function pollMigrationPlanRun(
  runId: string,
  assessment: RepositoryAssessment,
  signal?: AbortSignal,
): Promise<MigrationPlanningResult> {
  const startedAt = Date.now();
  const timeoutMs = 5 * 60 * 1000;
  const intervalMs = 2_000;

  while (Date.now() - startedAt < timeoutMs) {
    if (signal?.aborted) {
      throw new DOMException('The operation was aborted.', 'AbortError');
    }
    const pollResponse = await fetchMigrationPlanner(
      `/api/migration-plans/runs/${encodeURIComponent(runId)}`,
      { signal },
    );
    const pollPayload = await readJson(pollResponse);
    if (!pollResponse.ok) {
      throw new Error(problemMessage(pollPayload).replace(/^Assessment failed\.$/, 'Migration planning failed.'));
    }
    if (!isRecord(pollPayload)) {
      throw new Error('The migration planner returned an invalid poll response.');
    }
    const status = pollPayload.status;
    if (status === 'completed') {
      if (!isMigrationPlanningResult(pollPayload)) {
        throw new Error('The migration planner returned an invalid completed response.');
      }
      if (pollPayload.plan.assessmentId !== assessment.assessmentId
          || pollPayload.score.assessmentId !== assessment.assessmentId) {
        throw new Error('The migration planner returned a result for a different assessment.');
      }
      return pollPayload;
    }
    if (status === 'failed') {
      const errorObj = pollPayload.error;
      const title = isRecord(errorObj) && typeof errorObj.title === 'string' ? errorObj.title : 'Migration planning failed.';
      const errors = isRecord(errorObj) && Array.isArray(errorObj.errors) ? errorObj.errors.filter((e): e is string => typeof e === 'string') : [];
      const detail = errors.length > 0 ? `${title} (${errors.join('; ')})` : title;
      throw new Error(detail);
    }
    await waitForNextPoll(intervalMs, signal);
  }

  throw new Error(`Migration planning did not complete within ${Math.round(timeoutMs / 1000)}s.`);
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
  const response = await fetchAssessmentService('/api/assessments', {
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
  const response = await fetchAssessmentService('/api/auth/github/sessions', {
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
  const response = await fetchAssessmentService(`/api/auth/github/sessions/${encodeURIComponent(sessionId)}`, {
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
  const response = await fetchAssessmentService(`/api/auth/github/sessions/${encodeURIComponent(sessionId)}`, {
    method: 'DELETE',
    headers: { 'X-Arm-Migration-Client': 'dashboard' },
  });
  if (!response.ok && response.status !== 404) {
    throw new Error('GitHub sign-in could not be canceled.');
  }
}

// ---- Feature 3: migration actions runner ----

const migrationJobStatuses: readonly MigrationJobStatus[] =
  ['queued', 'running', 'completed', 'failed'];

const migrationJobStatusSet = new Set<string>(migrationJobStatuses);

function isMigrationJobStatus(value: unknown): value is MigrationJobStatus {
  return typeof value === 'string' && migrationJobStatusSet.has(value);
}

function isMigrationAcceptanceTest(value: unknown) {
  return isRecord(value)
    && typeof value.id === 'string'
    && typeof value.description === 'string'
    && typeof value.expectedOutcome === 'string';
}

function isGeneratedPatch(value: unknown): value is GeneratedPatch {
  return isRecord(value)
    && typeof value.workItemId === 'string'
    && typeof value.agentOrSkill === 'string'
    && typeof value.title === 'string'
    && typeof value.diff === 'string'
    && typeof value.originalSizeBytes === 'number'
    && typeof value.truncated === 'boolean'
    && isStringArray(value.evidenceIds)
    && Array.isArray(value.acceptanceTests)
    && value.acceptanceTests.every(isMigrationAcceptanceTest);
}

function isSkippedWorkItem(value: unknown): value is SkippedWorkItem {
  return isRecord(value)
    && typeof value.workItemId === 'string'
    && typeof value.agentOrSkill === 'string'
    && typeof value.reason === 'string';
}

function isPatchRejection(value: unknown): value is PatchRejection {
  return isRecord(value)
    && typeof value.id === 'string'
    && typeof value.reason === 'string';
}

function isBranchApplication(value: unknown): value is BranchApplication {
  return isRecord(value)
    && typeof value.worktreePath === 'string'
    && typeof value.branchName === 'string'
    && typeof value.branchHeadSha === 'string'
    && typeof value.commitCreated === 'boolean'
    && isStringArray(value.appliedIds)
    && Array.isArray(value.rejected)
    && value.rejected.every(isPatchRejection);
}

function isNullableString(value: unknown): value is string | null | undefined {
  return value === null || value === undefined || typeof value === 'string';
}

function isValidationDispatch(value: unknown): value is ValidationDispatch {
  return isRecord(value)
    && isNullableString(value.planId)
    && isNullableString(value.runId)
    && isNullableString(value.statusUrl)
    && typeof value.dispatched === 'boolean'
    && isNullableString(value.error);
}

function isArm64BuildDispatch(value: unknown): value is Arm64BuildDispatch {
  return isRecord(value)
    && isNullableString(value.jobId)
    && isNullableString(value.statusUrl)
    && typeof value.dispatched === 'boolean'
    && isNullableString(value.error);
}

function isMigrationActionsResult(value: unknown): value is MigrationActionsResult {
  return isRecord(value)
    && typeof value.planId === 'string'
    && typeof value.sourceCommitSha === 'string'
    && Array.isArray(value.generated)
    && value.generated.every(isGeneratedPatch)
    && Array.isArray(value.skipped)
    && value.skipped.every(isSkippedWorkItem)
    && (value.branch === null || value.branch === undefined || isBranchApplication(value.branch))
    && (value.validation === null || value.validation === undefined || isValidationDispatch(value.validation))
    && (value.arm64Build === null || value.arm64Build === undefined || isArm64BuildDispatch(value.arm64Build));
}

function isMigrationJob(value: unknown): value is MigrationJob {
  if (!isRecord(value)) return false;
  return typeof value.jobId === 'string'
    && isMigrationJobStatus(value.status)
    && typeof value.planId === 'string'
    && isRecord(value.target)
    && typeof value.target.url === 'string'
    && typeof value.target.commitSha === 'string'
    && typeof value.createdAt === 'string'
    && isNullableString(value.startedAt)
    && isNullableString(value.finishedAt)
    && (value.result === null || value.result === undefined || isMigrationActionsResult(value.result))
    && isNullableString(value.error);
}

function isMigrationJobAccepted(value: unknown): value is MigrationJobAccepted {
  return isRecord(value)
    && typeof value.jobId === 'string'
    && typeof value.status === 'string'
    && typeof value.statusUrl === 'string';
}

function normalizeMigrationJob(value: unknown): MigrationJob {
  if (!isMigrationJob(value)) {
    throw new Error('The migration action runner returned an invalid response.');
  }
  const raw = value as unknown as Record<string, unknown>;
  return {
    jobId: value.jobId,
    status: value.status,
    planId: value.planId,
    target: value.target,
    createdAt: value.createdAt,
    startedAt: value.startedAt ?? null,
    finishedAt: value.finishedAt ?? null,
    result: (raw.result as MigrationActionsResult | null | undefined) ?? null,
    error: value.error ?? null,
  };
}

export async function submitMigrationJob(
  plan: MigrationPlanningResult['plan'],
  target: { url: string; commitSha: string },
  signal?: AbortSignal,
): Promise<MigrationJobAccepted> {
  const response = await fetchAutomationService('/api/migration-actions', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ plan, target }),
    signal,
  });
  const payload = await readJson(response);
  if (!response.ok) {
    throw new Error(
      problemMessage(payload).replace(/^Assessment failed\.$/, 'Migration actions were rejected.'),
    );
  }
  if (!isMigrationJobAccepted(payload)) {
    throw new Error('The migration action runner returned an invalid response.');
  }
  return payload;
}

export async function getMigrationJob(
  jobId: string,
  signal?: AbortSignal,
): Promise<MigrationJob> {
  const response = await fetchAutomationService(
    `/api/migration-actions/jobs/${encodeURIComponent(jobId)}`,
    { signal },
  );
  const payload = await readJson(response);
  if (!response.ok) {
    throw new Error(
      problemMessage(payload).replace(/^Assessment failed\.$/, 'Migration job status is unavailable.'),
    );
  }
  return normalizeMigrationJob(payload);
}

const arm64RunStatuses = new Set<Arm64RunStatus['status']>(['queued', 'running', 'completed', 'failed', 'cancelled']);

function isArm64RunStatus(value: unknown): value is Arm64RunStatus {
  if (!isRecord(value)) return false;
  return typeof value.jobId === 'string'
    && typeof value.status === 'string'
    && arm64RunStatuses.has(value.status as Arm64RunStatus['status'])
    && typeof value.createdAt === 'string'
    && isNullableString(value.startedAt)
    && isNullableString(value.finishedAt)
    && (value.scorecard === null || value.scorecard === undefined || isRecord(value.scorecard))
    && isNullableString(value.error);
}

export async function getArm64Run(
  runId: string,
  signal?: AbortSignal,
): Promise<Arm64RunStatus> {
  const response = await fetchAutomationService(
    `/api/migration-actions/arm64-runs/${encodeURIComponent(runId)}`,
    { signal },
  );
  const payload = await readJson(response);
  if (!response.ok) {
    throw new Error(
      problemMessage(payload).replace(/^Assessment failed\.$/, 'ARM64 run status is unavailable.'),
    );
  }
  if (!isArm64RunStatus(payload)) {
    throw new Error('The ARM64 runner returned an invalid response.');
  }
  return payload as Arm64RunStatus;
}

export async function pollArm64Run(
  runId: string,
  options: {
    onUpdate?: (run: Arm64RunStatus) => void;
    signal?: AbortSignal;
    intervalMs?: number;
  } = {},
): Promise<Arm64RunStatus> {
  const interval = options.intervalMs ?? 3000;
  let latest = await getArm64Run(runId, options.signal);
  options.onUpdate?.(latest);
  while (latest.status === 'queued' || latest.status === 'running') {
    await waitForNextPoll(interval, options.signal);
    latest = await getArm64Run(runId, options.signal);
    options.onUpdate?.(latest);
  }
  return latest;
}

function waitForNextPoll(intervalMs: number, signal?: AbortSignal) {
  return new Promise<void>((resolve, reject) => {
    if (signal?.aborted) {
      reject(new DOMException('The operation was aborted.', 'AbortError'));
      return;
    }
    const timer = setTimeout(() => {
      signal?.removeEventListener('abort', onAbort);
      resolve();
    }, intervalMs);
    const onAbort = () => {
      clearTimeout(timer);
      signal?.removeEventListener('abort', onAbort);
      reject(new DOMException('The operation was aborted.', 'AbortError'));
    };
    signal?.addEventListener('abort', onAbort, { once: true });
  });
}

export async function pollMigrationJob(
  jobId: string,
  options: { onUpdate?: (job: MigrationJob) => void; signal?: AbortSignal; intervalMs?: number } = {},
): Promise<MigrationJob> {
  const interval = options.intervalMs ?? 1500;
  let latest = await getMigrationJob(jobId, options.signal);
  options.onUpdate?.(latest);
  while (latest.status === 'queued' || latest.status === 'running') {
    await waitForNextPoll(interval, options.signal);
    latest = await getMigrationJob(jobId, options.signal);
    options.onUpdate?.(latest);
  }
  return latest;
}

// ---- Feature 4: validation runs and reports ----

const validationRunStatuses: readonly ValidationRunStatus[] =
  ['queued', 'running', 'completed', 'failed', 'cancelled'];

const validationRunStatusSet = new Set<string>(validationRunStatuses);

function isValidationRunStatus(value: unknown): value is ValidationRunStatus {
  return typeof value === 'string' && validationRunStatusSet.has(value);
}

const criterionResultStatuses: readonly CriterionResultStatus[] =
  ['passed', 'failed', 'not-run', 'inconclusive', 'skipped'];

const criterionResultStatusSet = new Set<string>(criterionResultStatuses);

function isCriterionResultStatus(value: unknown): value is CriterionResultStatus {
  return typeof value === 'string' && criterionResultStatusSet.has(value);
}

const overallStatusValues: readonly OverallScorecardStatus[] =
  ['validated', 'validation-failed', 'partially-validated', 'not-validated'];

const overallStatusSet = new Set<string>(overallStatusValues);

function isOverallStatus(value: unknown): value is OverallScorecardStatus {
  return typeof value === 'string' && overallStatusSet.has(value);
}

function isValidationRunSummary(value: unknown): value is ValidationRunSummary {
  if (!isRecord(value)) return false;
  return typeof value.runId === 'string'
    && typeof value.planId === 'string'
    && isValidationRunStatus(value.status)
    && typeof value.createdAt === 'string'
    && isNullableString(value.startedAt)
    && isNullableString(value.finishedAt)
    && isNullableString(value.summary)
    && isNullableString(value.error);
}

function isCriterionResult(value: unknown): value is CriterionResult {
  if (!isRecord(value)) return false;
  if (!isRecord(value.criterion)) return false;
  return typeof value.criterion.key === 'string'
    && typeof value.criterion.category === 'string'
    && typeof value.criterion.description === 'string'
    && typeof value.criterion.expectedOutcome === 'string'
    && isCriterionResultStatus(value.status)
    && typeof value.reason === 'string'
    && isStringArray(value.commandIds)
    && isStringArray(value.evidenceIds);
}

function isCoverageGap(value: unknown): value is ValidationCoverageGap {
  return isRecord(value)
    && typeof value.id === 'string'
    && typeof value.description === 'string'
    && isStringArray(value.criterionKeys);
}

function isScorecard(value: unknown): value is ValidationScorecard {
  return isRecord(value)
    && isOverallStatus(value.status)
    && typeof value.passed === 'number'
    && typeof value.failed === 'number'
    && typeof value.notRun === 'number'
    && typeof value.inconclusive === 'number'
    && typeof value.skipped === 'number'
    && Array.isArray(value.criteria)
    && value.criteria.every(isCriterionResult);
}

function isValidationReport(value: unknown): value is ValidationReport {
  return isRecord(value)
    && typeof value.runId === 'string'
    && typeof value.planFingerprint === 'string'
    && typeof value.migrationPlanId === 'string'
    && isScorecard(value.scorecard)
    && Array.isArray(value.coverageGaps)
    && value.coverageGaps.every(isCoverageGap);
}

function normalizeValidationRun(value: unknown): ValidationRunSummary {
  if (!isValidationRunSummary(value)) {
    throw new Error('The validation service returned an invalid response.');
  }
  return {
    runId: value.runId,
    planId: value.planId,
    status: value.status,
    createdAt: value.createdAt,
    startedAt: value.startedAt ?? null,
    finishedAt: value.finishedAt ?? null,
    summary: value.summary ?? null,
    error: value.error ?? null,
  };
}

export async function getValidationRun(
  runId: string,
  signal?: AbortSignal,
): Promise<ValidationRunSummary> {
  const response = await fetchValidationService(
    `/api/v1/validation/runs/${encodeURIComponent(runId)}`,
    { signal },
  );
  const payload = await readJson(response);
  if (!response.ok) {
    throw new Error(
      problemMessage(payload).replace(/^Assessment failed\.$/, 'Validation run status is unavailable.'),
    );
  }
  return normalizeValidationRun(payload);
}

export async function pollValidationRun(
  runId: string,
  options: {
    onUpdate?: (run: ValidationRunSummary) => void;
    signal?: AbortSignal;
    intervalMs?: number;
  } = {},
): Promise<ValidationRunSummary> {
  const interval = options.intervalMs ?? 2000;
  let latest = await getValidationRun(runId, options.signal);
  options.onUpdate?.(latest);
  while (latest.status === 'queued' || latest.status === 'running') {
    await waitForNextPoll(interval, options.signal);
    latest = await getValidationRun(runId, options.signal);
    options.onUpdate?.(latest);
  }
  return latest;
}

export async function getValidationReport(
  runId: string,
  signal?: AbortSignal,
): Promise<ValidationReport> {
  const response = await fetchValidationService(
    `/api/v1/validation/runs/${encodeURIComponent(runId)}/report`,
    { signal },
  );
  const payload = await readJson(response);
  if (!response.ok) {
    throw new Error(
      problemMessage(payload).replace(/^Assessment failed\.$/, 'Validation report is unavailable.'),
    );
  }
  if (!isValidationReport(payload)) {
    throw new Error('The validation service returned an invalid report.');
  }
  return payload;
}