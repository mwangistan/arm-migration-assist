import { describe, expect, it } from 'vitest';
import { foldArm64Evidence } from './App';
import type {
  Arm64RunStatus,
  Arm64StepOutcome,
  CriterionResult,
  CriterionResultStatus,
  ValidationReport,
} from './types';

function criterion(key: string, category: string, status: CriterionResultStatus): CriterionResult {
  return {
    criterion: {
      key,
      source: key.split(':')[0],
      sourceId: key,
      workItemId: null,
      category,
      description: `${category} check`,
      expectedOutcome: 'passes',
    },
    status,
    reason: 'No executable mapping or measured evidence; manual/untested criteria remain not-run.',
    commandIds: [],
    evidenceIds: [],
  };
}

function report(criteria: CriterionResult[]): ValidationReport {
  const count = (s: CriterionResultStatus) => criteria.filter((c) => c.status === s).length;
  return {
    schemaVersion: 'ValidationReportV1',
    runId: 'run-1',
    planFingerprint: 'fp',
    migrationPlanId: 'plan-1',
    scorecard: {
      status: 'not-validated',
      passed: count('passed'),
      failed: count('failed'),
      notRun: count('not-run'),
      inconclusive: count('inconclusive'),
      skipped: count('skipped'),
      criteria,
    },
    coverageGaps: [],
  };
}

function outcome(succeeded: boolean, exitCode: number): Arm64StepOutcome {
  return {
    tool: 'python',
    command: 'python -m build',
    workingDirectory: '/repo',
    exitCode,
    succeeded,
    durationSeconds: 42,
    stdoutTail: '',
    stderrTail: succeeded ? '' : 'boom',
  };
}

function run(
  status: Arm64RunStatus['status'],
  build: Arm64StepOutcome | null,
  tests: Arm64StepOutcome | null,
): Arm64RunStatus {
  return {
    jobId: 'arm64-job-1',
    status,
    createdAt: '2026-09-21T00:00:00Z',
    startedAt: '2026-09-21T00:00:01Z',
    finishedAt: '2026-09-21T00:05:00Z',
    progress: null,
    scorecard: {
      hardware: {
        vmSku: 'Standard_D4ps_v5',
        region: 'eastus2',
        architecture: 'arm64',
        kernel: 'linux',
        cpuModel: 'Ampere',
        cpuCount: 4,
        memoryMB: 16384,
      },
      sourceCommitSha: 'abc',
      resolvedCommitSha: 'abc',
      patchApplication: { applied: [], rejected: [] },
      build,
      tests,
      wallClockSeconds: 300,
      summary: 'done',
    },
    error: null,
  };
}

const baseCriteria = () => [
  criterion('validation:vc-arm64-build-check', 'build', 'not-run'),
  criterion('validation:vc-arm64-functional-check', 'functional', 'not-run'),
  criterion('acceptance:wi-audit:at-report', 'acceptance', 'not-run'),
];

describe('foldArm64Evidence', () => {
  it('folds a successful runner build + test into the generic validation criteria only', () => {
    const merged = foldArm64Evidence(
      report(baseCriteria()),
      run('completed', outcome(true, 0), outcome(true, 0)),
    );

    const byKey = new Map(merged.scorecard.criteria.map((c) => [c.criterion.key, c]));
    expect(byKey.get('validation:vc-arm64-build-check')?.status).toBe('passed');
    expect(byKey.get('validation:vc-arm64-functional-check')?.status).toBe('passed');
    // Per-package acceptance criteria are left untouched — one runner build does not prove them.
    expect(byKey.get('acceptance:wi-audit:at-report')?.status).toBe('not-run');

    expect(merged.scorecard.passed).toBe(2);
    // Counts and status reflect only the machine-measured `validation:` criteria;
    // the manual `acceptance:` item is excluded from the headline (still present in criteria).
    expect(merged.scorecard.notRun).toBe(0);
    expect(merged.scorecard.status).toBe('validated');
    expect(byKey.get('validation:vc-arm64-build-check')?.evidenceIds).toContain('arm64-run:arm64-job-1');
  });

  it('marks the build criterion failed when the runner build fails', () => {
    const merged = foldArm64Evidence(
      report(baseCriteria()),
      run('failed', outcome(false, 1), null),
    );

    const byKey = new Map(merged.scorecard.criteria.map((c) => [c.criterion.key, c]));
    expect(byKey.get('validation:vc-arm64-build-check')?.status).toBe('failed');
    expect(merged.scorecard.failed).toBe(1);
    expect(merged.scorecard.status).toBe('validation-failed');
  });

  it('excludes manual acceptance criteria from headline counts and status', () => {
    // Build passes, functional not measured (tests null), plus a manual acceptance item.
    const merged = foldArm64Evidence(
      report(baseCriteria()),
      run('completed', outcome(true, 0), null),
    );
    // measured = [build passed, functional not-run]; manual acceptance excluded.
    expect(merged.scorecard.passed).toBe(1);
    expect(merged.scorecard.notRun).toBe(1); // functional only, NOT the acceptance item
    expect(merged.scorecard.status).toBe('partially-validated');
  });

  it('leaves the report unchanged while the runner is still in flight', () => {
    const original = report(baseCriteria());
    const merged = foldArm64Evidence(original, run('running', null, null));
    expect(merged).toBe(original);
  });

  it('does not overwrite a criterion that already has a measured status', () => {
    const criteria = [
      criterion('validation:vc-arm64-build-check', 'build', 'passed'),
      criterion('validation:vc-arm64-functional-check', 'functional', 'not-run'),
    ];
    const merged = foldArm64Evidence(
      report(criteria),
      run('completed', outcome(false, 2), outcome(true, 0)),
    );
    const byKey = new Map(merged.scorecard.criteria.map((c) => [c.criterion.key, c]));
    // Build was already passed → not clobbered by the runner's later status.
    expect(byKey.get('validation:vc-arm64-build-check')?.status).toBe('passed');
    expect(byKey.get('validation:vc-arm64-functional-check')?.status).toBe('passed');
  });
});
