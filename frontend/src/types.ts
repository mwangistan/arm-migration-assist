export interface Evidence {
  sourceType: 'file' | 'manifest' | 'binary' | 'config' | 'ci-log' | 'documentation' | 'artifact';
  path?: string;
  artifact?: string;
  observation: string;
}

export interface DependencyFinding {
  evidenceId: string;
  name: string;
  version?: string | null;
  ecosystem: string;
  type: 'managed' | 'native' | 'com' | 'plugin' | 'driver' | 'unknown';
  criticality: 'required' | 'optional';
  architectureStatus: 'ready' | 'emulation-only' | 'unknown' | 'blocked';
  availableArchitectures: string[];
  replacementCandidates: string[];
  evidence: Evidence[];
  confidence: number;
}

export interface CodeFinding {
  evidenceId: string;
  ruleId: string;
  category: string;
  severity: 'critical' | 'high' | 'medium' | 'low' | 'informational';
  file: string;
  line?: number | null;
  column?: number | null;
  description: string;
  evidence: Evidence[];
  confidence: number;
}

export interface AssessmentProducer {
  name: string;
  version: string;
  ruleset?: string | null;
  scannerVersions?: Array<{
    name: string;
    version: string;
  }>;
}

export interface AvailableSkill {
  name: string;
  version: string;
  description: string;
  writeAccess: boolean;
  supportedInputs: string[];
  supportedOutputs: string[];
}

export interface RepositoryAssessment {
  schemaVersion: string;
  assessmentId: string;
  generatedAt: string;
  producer: AssessmentProducer;
  repository: {
    name: string;
    url: string;
    commitSha: string;
    defaultBranch: string;
    license?: string | null;
  };
  technology: {
    languages: string[];
    frameworks: string[];
    projectTypes: string[];
    buildSystems: string[];
    packageManagers: string[];
    installers: string[];
    ciSystems: string[];
  };
  dependencies: DependencyFinding[];
  codeFindings: CodeFinding[];
  buildFindings: {
    evidenceId: string;
    arm64TargetExists: boolean;
    arm64EcTargetExists: boolean;
    arm64CiJobExists: boolean;
    packagingSupportsArm64: boolean;
    testsExist: boolean;
    detectedTargets: string[];
    evidence: Evidence[];
  };
  windowsExperience: {
    windowsVersionExists: boolean;
    uiTechnology: string;
    installerExists: boolean;
    offlineCapable: boolean;
    accessibilityEvidence: string;
    accessibilityNotes?: string | null;
    notificationsIntegrated: boolean;
    lifecycleIntegrated: boolean;
    evidence: Evidence[];
  };
  scanCoverage: {
    filesScanned: number;
    filesTotal: number;
    dependencyResolutionRate: number;
    scannersCompleted: string[];
    scannersFailed: string[];
  };
  unknowns: Array<{
    description: string;
    area: string;
    requiredSkill?: string | null;
    evidenceIds?: string[];
  }>;
  availableSkills: AvailableSkill[];
}

export interface ReadinessDimension {
  dimensionKey: string;
  weightPct: number;
  rawScore: number;
  weightedContribution: number;
  confidence: number;
  rationaleCodes: string[];
  evidenceIds: string[];
}

export interface ReadinessScore {
  schemaVersion: string;
  assessmentId: string;
  generatedAt: string;
  overallScore: number;
  uncappedScore: number;
  band: string;
  evidenceCompleteness: string;
  evidenceCompletenessScore: number;
  provisional: boolean;
  provisionalReasons: string[];
  dimensions: ReadinessDimension[];
  capsApplied: Array<{
    capId: string;
    ceiling: number;
    description: string;
    triggeredBy: string[];
  }>;
  majorBlockers: Array<{
    blockerId: string;
    category: string;
    description: string;
    evidenceIds: string[];
  }>;
  scoreSummary: string;
}

export interface MigrationAcceptanceTest {
  id: string;
  description: string;
  expectedOutcome: string;
}

export interface MigrationWorkItem {
  id: string;
  sequence: number;
  priority: string;
  title: string;
  objective: string;
  agentOrSkill: string;
  inputs: string[];
  expectedOutputs: string[];
  dependencies: string[];
  evidenceIds: string[];
  guidanceIds: string[];
  acceptanceTests: MigrationAcceptanceTest[];
  approvalRequired: boolean;
  estimatedEffort: string;
  risk: string;
}

export interface MigrationValidationCheck {
  id: string;
  description: string;
  expectedOutcome: string;
}

export interface MigrationRisk {
  id: string;
  description: string;
  severity: string;
  mitigation: string;
}

export interface MigrationUnknown {
  id: string;
  description: string;
  requiredSkill?: string | null;
}

export interface MissingMigrationSkill {
  proposedName: string;
  purpose: string;
  justification: string;
  writeAccess?: boolean;
}

export interface MigrationApproval {
  approvalId: string;
  summary: string;
  workItemIds: string[];
}

export interface MigrationValidationPlan {
  targetDevices: string[];
  buildChecks: MigrationValidationCheck[];
  functionalChecks: MigrationValidationCheck[];
  reliabilityChecks: MigrationValidationCheck[];
  performanceChecks: MigrationValidationCheck[];
  powerChecks: MigrationValidationCheck[];
  offlineChecks: MigrationValidationCheck[];
  accessibilityChecks: MigrationValidationCheck[];
  windowsExperienceChecks: MigrationValidationCheck[];
}

export interface MigrationPlan {
  schemaVersion: string;
  planId: string;
  assessmentId: string;
  generatedAt: string;
  modelProvenance: {
    provider: string;
    name: string;
    version: string;
  };
  recommendedPath: string;
  confidence: string;
  executiveSummary: string;
  scoreInterpretation: string;
  workItems: MigrationWorkItem[];
  alternatives: Array<{
    path: string;
    disposition: string;
    rationale: string;
    estimatedEffort?: string;
    risk?: string;
  }>;
  risks: MigrationRisk[];
  unknowns: MigrationUnknown[];
  missingSkills: MissingMigrationSkill[];
  requiredApprovals: MigrationApproval[];
  validationPlan: MigrationValidationPlan;
  [key: string]: unknown;
}

export interface MigrationPlanningResult {
  runId: string;
  plan: MigrationPlan;
  score: ReadinessScore;
  warnings: string[];
}

// ---- Feature 3: reviewable changes ----

export interface GeneratedPatch {
  workItemId: string;
  agentOrSkill: string;
  title: string;
  diff: string;
  originalSizeBytes: number;
  truncated: boolean;
  evidenceIds: string[];
  acceptanceTests: MigrationAcceptanceTest[];
}

export interface SkippedWorkItem {
  workItemId: string;
  agentOrSkill: string;
  reason: string;
}

export interface PatchRejection {
  id: string;
  reason: string;
}

export interface BranchApplication {
  worktreePath: string;
  branchName: string;
  branchHeadSha: string;
  commitCreated: boolean;
  appliedIds: string[];
  rejected: PatchRejection[];
}

// F3 → F4 dispatch envelope populated by the composed host once F3 has committed a branch.
export interface ValidationDispatch {
  planId: string | null;
  runId: string | null;
  statusUrl: string | null;
  dispatched: boolean;
  error: string | null;
}

// F3 → ARM64 runner VM dispatch envelope. Populated by the composed host after F3
// generates patches. The runner clones the source repo, applies the patches, and
// runs a real build+test on Ampere Cobalt 100 Arm64 hardware in Azure.
export interface Arm64BuildDispatch {
  jobId: string | null;
  statusUrl: string | null;
  dispatched: boolean;
  error: string | null;
}

export interface Arm64Scorecard {
  hardware: {
    vmSku: string;
    region: string;
    architecture: string;
    kernel: string;
    cpuModel: string;
    cpuCount: number;
    memoryMB: number;
  };
  sourceCommitSha: string;
  resolvedCommitSha: string;
  patchApplication: {
    applied: string[];
    rejected: { id: string; reason: string }[];
  };
  build: Arm64StepOutcome | null;
  tests: Arm64StepOutcome | null;
  wallClockSeconds: number;
  summary: string;
}

export interface Arm64StepOutcome {
  tool: string;
  command: string;
  workingDirectory: string;
  exitCode: number;
  succeeded: boolean;
  durationSeconds: number;
  stdoutTail: string;
  stderrTail: string;
}

export interface Arm64ProgressStep {
  key: string;
  label: string;
  status: 'pending' | 'running' | 'succeeded' | 'failed' | 'skipped';
  detail: string | null;
  startedAt: string | null;
  finishedAt: string | null;
}

export interface Arm64Progress {
  phase: string;
  steps: Arm64ProgressStep[];
  percent: number;
}

export interface Arm64RunStatus {
  jobId: string;
  status: 'queued' | 'running' | 'completed' | 'failed' | 'cancelled';
  createdAt: string;
  startedAt: string | null;
  finishedAt: string | null;
  progress: Arm64Progress | null;
  scorecard: Arm64Scorecard | null;
  error: string | null;
}

export interface MigrationActionsResult {
  planId: string;
  sourceCommitSha: string;
  generated: GeneratedPatch[];
  skipped: SkippedWorkItem[];
  branch: BranchApplication | null;
  validation: ValidationDispatch | null;
  arm64Build: Arm64BuildDispatch | null;
}

export type MigrationJobStatus = 'queued' | 'running' | 'completed' | 'failed';

export interface MigrationJob {
  jobId: string;
  status: MigrationJobStatus;
  planId: string;
  target: { url: string; commitSha: string };
  createdAt: string;
  startedAt: string | null;
  finishedAt: string | null;
  result: MigrationActionsResult | null;
  error: string | null;
}

export interface MigrationJobAccepted {
  jobId: string;
  status: string;
  statusUrl: string;
}

// ---- Feature 4: validation run + scorecard ----

export type ValidationRunStatus = 'queued' | 'running' | 'completed' | 'failed' | 'cancelled';

export interface ValidationRunSummary {
  runId: string;
  planId: string;
  status: ValidationRunStatus;
  createdAt: string;
  startedAt: string | null;
  finishedAt: string | null;
  summary: string | null;
  error: string | null;
}

export interface ValidationCriterion {
  key: string;
  source: string;
  sourceId: string;
  workItemId: string | null;
  category: string;
  description: string;
  expectedOutcome: string;
}

export type CriterionResultStatus = 'passed' | 'failed' | 'not-run' | 'inconclusive' | 'skipped';

export interface CriterionResult {
  criterion: ValidationCriterion;
  status: CriterionResultStatus;
  reason: string;
  commandIds: string[];
  evidenceIds: string[];
}

export type OverallScorecardStatus =
  | 'validated'
  | 'validation-failed'
  | 'partially-validated'
  | 'not-validated';

export interface ValidationScorecard {
  status: OverallScorecardStatus;
  passed: number;
  failed: number;
  notRun: number;
  inconclusive: number;
  skipped: number;
  criteria: CriterionResult[];
}

export interface ValidationCoverageGap {
  id: string;
  description: string;
  criterionKeys: string[];
}

export interface ValidationReport {
  schemaVersion: string;
  runId: string;
  planFingerprint: string;
  migrationPlanId: string;
  scorecard: ValidationScorecard;
  coverageGaps: ValidationCoverageGap[];
}
