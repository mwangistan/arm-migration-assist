import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import App from './App';
import type { RepositoryAssessment } from './types';

const assessment: RepositoryAssessment = {
  schemaVersion: '1.0',
  assessmentId: 'assessment-ui-test',
  generatedAt: '2026-09-16T00:00:00Z',
  producer: {
    name: 'repository-discovery',
    version: '1.0.0',
    ruleset: 'repository-discovery-1.0',
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

describe('App', () => {
  afterEach(() => {
    cleanup();
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  it('runs an assessment and presents dependency and code findings', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify(assessment), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    })));
    render(<App />);

    fireEvent.change(screen.getByLabelText('GitHub repository URL'), {
      target: { value: 'https://github.com/example/sample-app' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Run assessment' }));

    expect(await screen.findByRole('heading', { name: 'sample-app' })).toBeInTheDocument();
    expect(screen.getByText('1 dependency')).toBeInTheDocument();
    expect(screen.getByText('1 code finding')).toBeInTheDocument();
    expect(screen.getByText('native-runtime-x64')).toBeInTheDocument();
    expect(screen.getByText('NativeMethods.cs')).toBeInTheDocument();

    const createObjectURL = vi.fn().mockReturnValue('blob:assessment');
    const revokeObjectURL = vi.fn();
    vi.stubGlobal('URL', { createObjectURL, revokeObjectURL });
    let downloadedFileName = '';
    const anchorClick = vi.spyOn(HTMLAnchorElement.prototype, 'click')
      .mockImplementation(function captureDownload(this: HTMLAnchorElement) {
        downloadedFileName = this.download;
      });

    fireEvent.click(screen.getByRole('button', { name: 'Export JSON' }));

    expect(createObjectURL).toHaveBeenCalledWith(expect.any(Blob));
    expect(downloadedFileName).toBe('sample-app-assessment.json');
    expect(anchorClick).toHaveBeenCalledOnce();
    expect(revokeObjectURL).toHaveBeenCalledWith('blob:assessment');
  });

  it('clears an earlier result when a replacement assessment fails', async () => {
    vi.stubGlobal('fetch', vi.fn()
      .mockResolvedValueOnce(new Response(JSON.stringify(assessment), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }))
      .mockResolvedValueOnce(new Response(JSON.stringify({
        detail: 'The repository could not be assessed.',
      }), {
        status: 422,
        headers: { 'Content-Type': 'application/problem+json' },
      })));
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
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('[]', {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    })));
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
});