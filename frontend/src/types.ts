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