"""Validate RepositoryAssessmentV1.schema.json and prove the hardening rules bite.

Runs three passes:
    1. Meta-validate the schema against Draft 2020-12.
    2. Validate a minimal well-formed sample assessment.
    3. Feed a battery of intentionally-broken samples and require them to be rejected.

Run: python tools/pptx/validate_repository_assessment_schema.py
(placed under tools/pptx/ for convenience alongside other repo tooling)
"""

from __future__ import annotations

import copy
import json
import sys
from pathlib import Path

from jsonschema import Draft202012Validator, FormatChecker

REPO_ROOT = Path(__file__).resolve().parents[2]
SCHEMA_PATH = REPO_ROOT / "backend" / "MigrationPlanner" / "contracts" / "RepositoryAssessmentV1.schema.json"


VALID_SAMPLE = {
    "schemaVersion": "1.0",
    "assessmentId": "asm-2026-09-15-comfyui-001",
    "generatedAt": "2026-09-15T18:00:00Z",
    "producer": {
        "name": "arm-migration-assist-feature1",
        "version": "0.1.0",
        "ruleset": "woa-ruleset-2026-09",
        "scannerVersions": [
            {"name": "dependency-scanner", "version": "0.1.0"},
            {"name": "code-compatibility", "version": "0.1.0+build.42"},
        ],
    },
    "repository": {
        "name": "ComfyUI",
        "url": "https://github.com/Comfy-Org/ComfyUI",
        "commitSha": "36da3ff763687eab86a35e1019995dd1fb369b0d",
        "defaultBranch": "master",
        "license": "GPL-3.0",
    },
    "technology": {
        "languages": ["python"],
        "frameworks": ["pytorch"],
        "projectTypes": ["desktop", "service"],
        "buildSystems": ["setuptools"],
        "packageManagers": ["pip"],
        "installers": [],
        "ciSystems": ["github-actions"],
    },
    "dependencies": [
        {
            "evidenceId": "ev-dep-torch",
            "name": "torch",
            "version": "2.7.0",
            "ecosystem": "pypi",
            "type": "managed",
            "criticality": "required",
            "architectureStatus": "unknown",
            "availableArchitectures": ["x64", "arm64"],
            "replacementCandidates": [],
            "evidence": [
                {
                    "sourceType": "manifest",
                    "path": "requirements.txt",
                    "observation": "torch==2.7.0 pinned in requirements.txt line 5",
                }
            ],
            "confidence": 0.9,
        }
    ],
    "codeFindings": [
        {
            "evidenceId": "ev-code-simd-001",
            "ruleId": "ARM-CODE-SIMD-01",
            "category": "simd",
            "severity": "high",
            "file": "comfy/latent_preview.py",
            "line": 42,
            "description": "x86 SSE intrinsic reference in latent preview path.",
            "evidence": [
                {
                    "sourceType": "file",
                    "path": "comfy/latent_preview.py",
                    "observation": "imports numpy.core._simd; branch is x86-specific.",
                }
            ],
            "confidence": 0.8,
        }
    ],
    "buildFindings": {
        "evidenceId": "ev-build-001",
        "arm64TargetExists": False,
        "arm64EcTargetExists": False,
        "arm64CiJobExists": False,
        "packagingSupportsArm64": False,
        "testsExist": True,
        "detectedTargets": ["linux-x64", "windows-x64"],
        "evidence": [
            {
                "sourceType": "ci-log",
                "path": ".github/workflows/build.yml",
                "observation": "CI matrix lists linux-x64 and windows-x64 only.",
            }
        ],
    },
    "windowsExperience": {
        "windowsVersionExists": True,
        "uiTechnology": "web",
        "installerExists": True,
        "offlineCapable": True,
        "accessibilityEvidence": "unknown",
        "accessibilityNotes": None,
        "notificationsIntegrated": False,
        "lifecycleIntegrated": False,
        "evidence": [
            {
                "sourceType": "config",
                "path": "pyproject.toml",
                "observation": "Windows desktop entry point declared.",
            }
        ],
    },
    "scanCoverage": {
        "filesScanned": 4200,
        "filesTotal": 4200,
        "dependencyResolutionRate": 0.94,
        "scannersCompleted": [
            "dependency-scanner",
            "code-compatibility",
            "build-scanner",
            "windows-experience",
        ],
        "scannersFailed": [],
    },
    "unknowns": [],
    "availableSkills": [
        {
            "name": "assessment/repository-discovery",
            "version": "0.1.0",
            "description": "Detects languages, frameworks, and build systems.",
            "writeAccess": False,
            "supportedInputs": ["repositoryPath"],
            "supportedOutputs": ["technology", "buildFindings"],
        }
    ],
}


def _mutate(sample, path, value):
    node = sample
    for key in path[:-1]:
        node = node[key]
    node[path[-1]] = value
    return sample


def _delete(sample, path):
    node = sample
    for key in path[:-1]:
        node = node[key]
    del node[path[-1]]
    return sample


def _run_negative(schema, label, mutator):
    sample = copy.deepcopy(VALID_SAMPLE)
    sample = mutator(sample)
    v = Draft202012Validator(schema, format_checker=FormatChecker())
    errors = list(v.iter_errors(sample))
    if not errors:
        print(f"  FAIL [{label}] expected rejection, got none")
        return False
    print(f"  OK   [{label}] rejected: {errors[0].message[:120]}")
    return True


def main() -> int:
    schema = json.loads(SCHEMA_PATH.read_text(encoding="utf-8"))

    print("1) Meta-validate schema (Draft 2020-12)")
    Draft202012Validator.check_schema(schema)
    print("   OK\n")

    print("2) Positive sample")
    v = Draft202012Validator(schema, format_checker=FormatChecker())
    errors = list(v.iter_errors(VALID_SAMPLE))
    if errors:
        for e in errors:
            print(f"   FAIL: {list(e.absolute_path)} {e.message}")
        return 1
    print("   OK: minimal sample validates\n")

    print("3) Negative samples (each should be rejected)")
    checks = [
        (
            "schemaVersion 2.0 rejected",
            lambda s: _mutate(s, ["schemaVersion"], "2.0"),
        ),
        (
            "malformed generatedAt rejected",
            lambda s: _mutate(s, ["generatedAt"], "yesterday"),
        ),
        (
            "url without http(s) rejected",
            lambda s: _mutate(s, ["repository", "url"], "ftp://example.com/repo"),
        ),
        (
            "short commit SHA rejected",
            lambda s: _mutate(s, ["repository", "commitSha"], "36da3ff"),
        ),
        (
            "confidence > 1 rejected",
            lambda s: _mutate(s, ["dependencies", 0, "confidence"], 1.5),
        ),
        (
            "unknown dependency.type rejected",
            lambda s: _mutate(s, ["dependencies", 0, "type"], "wat"),
        ),
        (
            "unknown severity rejected",
            lambda s: _mutate(s, ["codeFindings", 0, "severity"], "catastrophic"),
        ),
        (
            "unknown uiTechnology rejected",
            lambda s: _mutate(s, ["windowsExperience", "uiTechnology"], "swift-ui"),
        ),
        (
            "unknown accessibilityEvidence rejected",
            lambda s: _mutate(
                s, ["windowsExperience", "accessibilityEvidence"], "somewhat"
            ),
        ),
        (
            "evidence with both path and artifact rejected",
            lambda s: _mutate(
                s,
                ["dependencies", 0, "evidence"],
                [
                    {
                        "sourceType": "manifest",
                        "path": "requirements.txt",
                        "artifact": "node-42",
                        "observation": "both fields set",
                    }
                ],
            ),
        ),
        (
            "extra top-level property rejected",
            lambda s: _mutate(s, ["hackerField"], "should-fail"),
        ),
        (
            "empty dependency evidence rejected",
            lambda s: _mutate(s, ["dependencies", 0, "evidence"], []),
        ),
        (
            "oversize description rejected",
            lambda s: _mutate(
                s, ["codeFindings", 0, "description"], "x" * 3000
            ),
        ),
        (
            "missing producer rejected",
            lambda s: _delete(s, ["producer"]),
        ),
        (
            "invalid producer version rejected",
            lambda s: _mutate(s, ["producer", "version"], "alpha"),
        ),
    ]

    passed = sum(_run_negative(schema, label, m) for label, m in checks)
    print(f"\n{passed}/{len(checks)} negative checks confirmed rejection")
    return 0 if passed == len(checks) else 2


if __name__ == "__main__":
    raise SystemExit(main())
