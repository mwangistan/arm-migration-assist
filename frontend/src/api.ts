import type { RepositoryAssessment } from './types';

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

export async function assessRepository(
  source: string,
  signal?: AbortSignal,
  authenticationSessionId?: string,
): Promise<RepositoryAssessment> {
  const response = await fetch('/api/assessments', {
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
  const response = await fetch('/api/auth/github/sessions', {
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
  const response = await fetch(`/api/auth/github/sessions/${encodeURIComponent(sessionId)}`, {
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
  const response = await fetch(`/api/auth/github/sessions/${encodeURIComponent(sessionId)}`, {
    method: 'DELETE',
    headers: { 'X-Arm-Migration-Client': 'dashboard' },
  });
  if (!response.ok && response.status !== 404) {
    throw new Error('GitHub sign-in could not be canceled.');
  }
}