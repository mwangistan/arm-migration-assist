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
  }>;
  risks: Array<Record<string, unknown>>;
  unknowns: Array<Record<string, unknown>>;
  missingSkills: Array<Record<string, unknown>>;
  requiredApprovals: Array<Record<string, unknown>>;
  validationPlan: Record<string, unknown>;
  [key: string]: unknown;
}

export interface MigrationPlanningResult {
  runId: string;
  plan: MigrationPlan;
  score: ReadinessScore;
  warnings: string[];
}