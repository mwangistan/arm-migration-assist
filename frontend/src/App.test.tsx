import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import App from './App';
import type { MigrationPlanningResult, RepositoryAssessment } from './types';

const assessment: RepositoryAssessment = {
  schemaVersion: '1.0',
  assessmentId: 'assessment-ui-test',
  generatedAt: '2026-09-16T00:00:00Z',
  producer: {
    name: 'repository-discovery',
    version: '1.0.0',
    ruleset: 'repository-discovery-1.2',
    scannerVersions: [],
  },
  repository: {
    name: 'sample-app',
    url: 'https://github.com/example/sample-app',
    commitSha: 'a'.repeat(40),
    defaultBranch: 'main',
    license: 'MIT',
  },
  technology: {
    languages: ['csharp', 'typescript'],
    frameworks: ['react'],
    projectTypes: ['web'],
    buildSystems: ['msbuild', 'vite'],
    packageManagers: ['nuget', 'npm'],
    installers: [],
    ciSystems: ['github-actions'],
  },
  dependencies: [
    {
      evidenceId: 'dependency-test',
      name: 'native-runtime-x64',
      version: '1.0.0',
      ecosystem: 'nuget',
      type: 'native',
      criticality: 'required',
      architectureStatus: 'emulation-only',
      availableArchitectures: ['x64'],
      replacementCandidates: [],
      confidence: 0.9,
      evidence: [{ sourceType: 'manifest', path: 'App.csproj', observation: 'Declared dependency.' }],
    },
  ],
  codeFindings: [
    {
      evidenceId: 'code-test',
      ruleId: 'ARM-CODE-PINVOKE-01',
      category: 'p-invoke',
      severity: 'medium',
      file: 'NativeMethods.cs',
      line: 8,
      column: null,
      description: 'A managed native-library import is declared.',
      evidence: [{ sourceType: 'file', path: 'NativeMethods.cs', observation: 'Detected declaration.' }],
      confidence: 0.95,
    },
  ],
  buildFindings: {
    evidenceId: 'build-test',
    arm64TargetExists: false,
    arm64EcTargetExists: false,
    arm64CiJobExists: false,
    packagingSupportsArm64: false,
    testsExist: true,
    detectedTargets: ['win-x64'],
    evidence: [],
  },
  windowsExperience: {
    windowsVersionExists: true,
    uiTechnology: 'web',
    installerExists: false,
    offlineCapable: false,
    accessibilityEvidence: 'partial',
    notificationsIntegrated: false,
    lifecycleIntegrated: false,
    evidence: [],
  },
  scanCoverage: {
    filesScanned: 42,
    filesTotal: 42,
    dependencyResolutionRate: 0,
    scannersCompleted: ['repository-intake', 'technology-discovery', 'dependency-scanner', 'code-compatibility-scanner'],
    scannersFailed: [],
  },
  unknowns: [{ description: 'One dependency needs verification.', area: 'dependency' }],
  availableSkills: [],
};

const planningResult: MigrationPlanningResult = {
  runId: 'run-ui-test',
  plan: {
    schemaVersion: '1.0',
    planId: 'plan-ui-test',
    assessmentId: assessment.assessmentId,
    generatedAt: '2026-09-16T00:01:00Z',
    modelProvenance: { provider: 'fake', name: 'fake-planner', version: '1.0.0' },
    recommendedPath: 'native-arm64',
    confidence: 'high',
    executiveSummary: 'Add ARM64 build coverage and verify the native dependency.',
    scoreInterpretation: 'Build and dependency evidence drive the recommendation.',
    workItems: [
      {
        id: 'wi-build-arm64',
        sequence: 1,
        priority: 'P0',
        title: 'Add an ARM64 build target',
        objective: 'Produce and validate the ARM64 build configuration.',
        agentOrSkill: 'build-config-generator',
        inputs: ['src/App.csproj'],
        expectedOutputs: ['patch'],
        dependencies: [],
        evidenceIds: ['build-test'],
        guidanceIds: [],
        acceptanceTests: [
          {
            id: 'at-build-arm64',
            description: 'Build the ARM64 target.',
            expectedOutcome: 'The Release ARM64 build succeeds.',
          },
        ],
        approvalRequired: true,
        estimatedEffort: 'small',
        risk: 'low',
      },
    ],
    alternatives: [{ path: 'arm64ec', disposition: 'deferred', rationale: 'Native ARM64 is viable.' }],
    risks: [],
    unknowns: [],
    missingSkills: [],
    requiredApprovals: [],
    validationPlan: {},
  },
  score: {
    schemaVersion: '1.0',
    assessmentId: assessment.assessmentId,
    generatedAt: '2026-09-16T00:01:00Z',
    overallScore: 74,
    uncappedScore: 74,
    band: 'moderate-migration',
    evidenceCompleteness: 'high',
    evidenceCompletenessScore: 0.92,
    provisional: false,
    provisionalReasons: [],
    dimensions: [
      ['dependency-compatibility', 30, 60],
      ['code-compatibility', 25, 75],
      ['build-and-ci-readiness', 20, 65],
      ['runtime-and-validation-evidence', 15, 85],
      ['windows-experience-and-deployment', 10, 90],
    ].map(([dimensionKey, weightPct, rawScore]) => ({
      dimensionKey: String(dimensionKey),
      weightPct: Number(weightPct),
      rawScore: Number(rawScore),
      weightedContribution: Number(rawScore) * Number(weightPct) / 100,
      confidence: 0.9,
      rationaleCodes: [],
      evidenceIds: [],
    })),
    capsApplied: [],
    majorBlockers: [],
    scoreSummary: 'The repository needs moderate ARM64 migration work.',
  },
  warnings: [],
};

function assessmentJob(
  status: 'queued' | 'running' | 'completed' | 'failed' | 'canceled' = 'completed',
  overrides: Record<string, unknown> = {},
) {
  return {
    jobId: 'c'.repeat(32),
    status,
    phase: status,
    percent: status === 'completed' ? 100 : 0,
    message: status === 'completed' ? 'Assessment completed.' : 'Assessment queued.',
    errorCode: null,
    error: null,
    statusUrl: `/api/assessment-jobs/${'c'.repeat(32)}`,
    eventsUrl: `/api/assessment-jobs/${'c'.repeat(32)}/events`,
    resultUrl: `/api/assessment-jobs/${'c'.repeat(32)}/result`,
    ...overrides,
  };
}

function jsonResponse(value: unknown, status = 200) {
  return new Response(JSON.stringify(value), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

describe('App', () => {
  beforeEach(() => {
    vi.stubGlobal('EventSource', undefined);
  });

  afterEach(() => {
    cleanup();
    vi.useRealTimers();
    vi.restoreAllMocks();
    vi.unstubAllEnvs();
    vi.unstubAllGlobals();
  });

  it('runs an assessment and presents dependency and code findings', async () => {
    vi.stubEnv('VITE_ASSESSMENT_API_URL', 'https://assessment.example.test/');
    vi.stubEnv('VITE_MIGRATION_PLANNER_API_URL', 'https://planner.example.test/');
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(jsonResponse(assessmentJob(), 202))
      .mockResolvedValueOnce(jsonResponse(assessment))
      .mockResolvedValueOnce(jsonResponse(planningResult));
    vi.stubGlobal('fetch', fetchMock);
    render(<App />);

    fireEvent.change(screen.getByLabelText('GitHub repository URL'), {
      target: { value: 'https://github.com/example/sample-app' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Run assessment' }));

    expect(await screen.findByRole('heading', { name: 'sample-app' })).toBeInTheDocument();
    expect(fetchMock.mock.calls[0][0]).toBe('https://assessment.example.test/api/assessment-jobs');
    expect(fetchMock.mock.calls[2][0]).toBe('https://planner.example.test/api/migration-plans');
    expect(screen.getByText('1 dependency')).toBeInTheDocument();
    expect(screen.getByText('1 code finding')).toBeInTheDocument();
    expect(screen.getByText('native-runtime-x64')).toBeInTheDocument();
    expect(screen.getByText('NativeMethods.cs')).toBeInTheDocument();
    expect(await screen.findByText('74')).toBeInTheDocument();
    expect(screen.getByText('Native Arm64')).toBeInTheDocument();
    expect(screen.getByText('Add an ARM64 build target')).toBeInTheDocument();
    expect(screen.getByText('The Release ARM64 build succeeds.')).toBeInTheDocument();

    const createObjectURL = vi.fn().mockReturnValue('blob:assessment');
    const revokeObjectURL = vi.fn();
    vi.stubGlobal('URL', { createObjectURL, revokeObjectURL });
    let downloadedFileName = '';
    let downloadedHref = '';
    const anchorClick = vi.spyOn(HTMLAnchorElement.prototype, 'click')
      .mockImplementation(function captureDownload(this: HTMLAnchorElement) {
        downloadedFileName = this.download;
        downloadedHref = this.href;
      });
    const print = vi.spyOn(window, 'print').mockImplementation(() => undefined);

    fireEvent.click(screen.getByRole('button', { name: 'Export JSON' }));

    expect(createObjectURL).toHaveBeenCalledWith(expect.any(Blob));
    expect(downloadedFileName).toBe('sample-app-assessment.json');
    expect(anchorClick).toHaveBeenCalledOnce();
    expect(revokeObjectURL).toHaveBeenCalledWith('blob:assessment');

    fireEvent.click(screen.getByRole('button', { name: 'Export plan' }));
    expect(downloadedFileName).toBe('sample-app-migration-plan.json');

    fireEvent.click(screen.getByRole('button', { name: 'Report .md' }));
    expect(downloadedFileName).toBe('migration-report-run-ui-test.md');
    expect(downloadedHref).toBe('https://planner.example.test/api/migration-plans/run-ui-test/report.md');

    fireEvent.click(screen.getByRole('button', { name: 'Report .html' }));
    expect(downloadedFileName).toBe('migration-report-run-ui-test.html');
    expect(downloadedHref).toBe('https://planner.example.test/api/migration-plans/run-ui-test/report.html');

    fireEvent.click(screen.getByRole('button', { name: 'Print report' }));
    expect(print).toHaveBeenCalledOnce();
  });

  it('clears an earlier result when a replacement assessment fails', async () => {
    vi.stubGlobal('fetch', vi.fn()
      .mockResolvedValueOnce(jsonResponse(assessmentJob(), 202))
      .mockResolvedValueOnce(jsonResponse(assessment))
      .mockResolvedValueOnce(jsonResponse(planningResult))
      .mockResolvedValueOnce(jsonResponse({
        detail: 'The repository could not be assessed.',
      }, 422)));
    render(<App />);

    const input = screen.getByLabelText('GitHub repository URL');
    fireEvent.change(input, { target: { value: 'https://github.com/example/sample-app' } });
    fireEvent.click(screen.getByRole('button', { name: 'Run assessment' }));
    expect(await screen.findByRole('heading', { name: 'sample-app' })).toBeInTheDocument();

    fireEvent.change(input, { target: { value: 'https://github.com/example/other-app' } });
    fireEvent.click(screen.getByRole('button', { name: 'Run assessment' }));

    expect(screen.queryByRole('heading', { name: 'sample-app' })).not.toBeInTheDocument();
    expect(await screen.findByText('The repository could not be assessed.')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Awaiting repository' })).toBeInTheDocument();
  });

  it('rejects a successful response that is not an assessment object', async () => {
    vi.stubGlobal('fetch', vi.fn()
      .mockResolvedValueOnce(jsonResponse(assessmentJob(), 202))
      .mockResolvedValueOnce(jsonResponse([])));
    render(<App />);

    fireEvent.change(screen.getByLabelText('GitHub repository URL'), {
      target: { value: 'https://github.com/example/invalid-response' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Run assessment' }));

    expect(await screen.findByText('The assessment service returned an invalid response.')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Awaiting repository' })).toBeInTheDocument();
  });

  it('handles a null problem response without crashing', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('null', {
      status: 422,
      headers: { 'Content-Type': 'application/problem+json' },
    })));
    render(<App />);

    fireEvent.change(screen.getByLabelText('GitHub repository URL'), {
      target: { value: 'https://github.com/example/null-problem' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Run assessment' }));

    expect(await screen.findByText('Assessment failed.')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Awaiting repository' })).toBeInTheDocument();
  });

  it('authenticates and resumes a protected repository assessment', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(jsonResponse(assessmentJob('failed', {
        errorCode: 'authentication-required',
        error: 'This repository requires GitHub sign-in.',
      }), 202))
      .mockResolvedValueOnce(jsonResponse({
        sessionId: 'a'.repeat(32),
        status: 'pending',
        expiresAt: '2026-09-16T01:00:00Z',
        message: 'Complete GitHub sign-in in the browser window.',
      }, 202))
      .mockResolvedValueOnce(jsonResponse({
        sessionId: 'a'.repeat(32),
        status: 'succeeded',
        expiresAt: '2026-09-16T01:00:00Z',
        message: 'GitHub sign-in completed.',
      }))
      .mockResolvedValueOnce(jsonResponse(assessmentJob(), 202))
      .mockResolvedValueOnce(jsonResponse(assessment))
      .mockResolvedValueOnce(jsonResponse(planningResult));
    vi.stubGlobal('fetch', fetchMock);
    render(<App />);

    fireEvent.change(screen.getByLabelText('GitHub repository URL'), {
      target: { value: 'https://github.com/example/private-app' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Run assessment' }));

    expect(await screen.findByRole('heading', { name: 'sample-app' })).toBeInTheDocument();
    expect(fetchMock).toHaveBeenCalledTimes(6);
    expect(JSON.parse(String(fetchMock.mock.calls[3][1]?.body))).toEqual({
      source: 'https://github.com/example/private-app',
      authenticationSessionId: 'a'.repeat(32),
    });
  });

  it('cancels an in-progress GitHub sign-in poll', async () => {
    const pendingSession = {
      sessionId: 'b'.repeat(32),
      status: 'pending',
      expiresAt: '2026-09-16T01:00:00Z',
      message: 'Complete GitHub sign-in in the browser window.',
    };
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(jsonResponse(assessmentJob('failed', {
        errorCode: 'authentication-required',
        error: 'This repository requires GitHub sign-in.',
      }), 202))
      .mockResolvedValueOnce(jsonResponse(pendingSession, 202))
      .mockResolvedValueOnce(jsonResponse(pendingSession))
      .mockResolvedValueOnce(new Response(null, { status: 204 }));
    vi.stubGlobal('fetch', fetchMock);
    render(<App />);

    fireEvent.change(screen.getByLabelText('GitHub repository URL'), {
      target: { value: 'https://github.com/example/private-app' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Run assessment' }));
    expect(await screen.findByText('Waiting for GitHub sign-in')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));

    expect(await screen.findByRole('heading', { name: 'Awaiting repository' })).toBeInTheDocument();
    expect(screen.queryByText('Waiting for GitHub sign-in')).not.toBeInTheDocument();
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(4));
    expect(fetchMock.mock.calls[3][0]).toBe(`/api/auth/github/sessions/${'b'.repeat(32)}`);
    expect(fetchMock.mock.calls[3][1]).toMatchObject({
      method: 'DELETE',
      headers: { 'X-Arm-Migration-Client': 'dashboard' },
    });
  });

  it('shows live assessment phases from the event stream', async () => {
    class MockEventSource {
      static readonly CLOSED = 2;
      static instance: MockEventSource;
      readyState = 1;
      onerror: (() => void) | null = null;
      private listener: ((event: MessageEvent<string>) => void) | null = null;

      constructor(readonly url: string) {
        MockEventSource.instance = this;
      }

      addEventListener(type: string, listener: EventListener) {
        if (type === 'assessment') this.listener = listener as (event: MessageEvent<string>) => void;
      }

      removeEventListener() {}
      close() { this.readyState = MockEventSource.CLOSED; }
      emit(payload: unknown) {
        this.listener?.(new MessageEvent('assessment', { data: JSON.stringify(payload) }));
      }
    }

    vi.stubGlobal('EventSource', MockEventSource);
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(jsonResponse(assessmentJob('queued'), 202))
      .mockResolvedValueOnce(jsonResponse(assessmentJob()))
      .mockResolvedValueOnce(jsonResponse(assessment))
      .mockResolvedValueOnce(jsonResponse(planningResult));
    vi.stubGlobal('fetch', fetchMock);
    render(<App />);

    fireEvent.change(screen.getByLabelText('GitHub repository URL'), {
      target: { value: 'https://github.com/example/sample-app' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Run assessment' }));

    await waitFor(() => expect(MockEventSource.instance).toBeDefined());
    MockEventSource.instance.emit({
      eventType: 'progress',
      phase: 'dependency-scanner',
      percent: 60,
      message: 'Scanning dependency manifests and binaries.',
    });

    expect(await screen.findByText('Dependency Scanner')).toBeInTheDocument();
    expect(screen.getByText('60%')).toBeInTheDocument();
    expect(screen.getByText('Scanning dependency manifests and binaries.')).toBeInTheDocument();

    MockEventSource.instance.emit({
      eventType: 'completed',
      phase: 'completed',
      percent: 100,
      message: 'Assessment completed.',
    });

    expect(await screen.findByText('Recommended path')).toBeInTheDocument();
  });
});