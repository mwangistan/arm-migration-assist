"""Validate MigrationPlanV1.schema.json and prove the hardening rules bite.

Runs three passes:
    1. Meta-validate the schema against Draft 2020-12.
    2. Validate a minimal well-formed sample plan.
    3. Feed a battery of intentionally-broken samples and require them to be rejected.

Run: python tools/pptx/validate_migration_plan_schema.py
"""

from __future__ import annotations

import copy
import json
from pathlib import Path

from jsonschema import Draft202012Validator, FormatChecker

REPO_ROOT = Path(__file__).resolve().parents[2]
SCHEMA_PATH = (
    REPO_ROOT
    / "backend"
    / "MigrationPlanner"
    / "contracts"
    / "MigrationPlanV1.schema.json"
)


VALID_SAMPLE = {
    "schemaVersion": "1.0",
    "planId": "plan-2026-09-15-comfyui-001",
    "assessmentId": "asm-2026-09-15-comfyui-001",
    "generatedAt": "2026-09-15T19:00:00Z",
    "modelProvenance": {
        "provider": "fake",
        "name": "fake-planner",
        "version": "0.1.0",
        "temperature": 0.0,
        "promptDigest": "a" * 64,
    },
    "scoreDigest": "b" * 64,
    "corpusVersion": "2026-09-15.1",
    "recommendedPath": "native-arm64",
    "confidence": "medium",
    "executiveSummary": (
        "ComfyUI is a mostly managed Python application with ARM64-capable "
        "dependencies. Native ARM64 is the recommended path once the build "
        "matrix and CI job are added."
    ),
    "scoreInterpretation": (
        "Score 78 (moderate-migration). Dependency compatibility drives the "
        "score; the missing ARM64 build target keeps the plan from reaching "
        "the ready band."
    ),
    "facts": [
        {
            "statement": "Dependency 'torch' is available for ARM64.",
            "evidenceIds": ["ev-dep-torch"],
            "guidanceIds": [],
        }
    ],
    "inferences": [
        {
            "statement": "Adding an ARM64 build matrix will lift readiness to the 'ready' band.",
            "evidenceIds": ["ev-build-001"],
            "guidanceIds": ["add-arm-support-01"],
            "confidence": 0.7,
        }
    ],
    "alternatives": [
        {
            "path": "arm64ec",
            "disposition": "deferred",
            "rationale": "No native dependency requires x64 interop; Arm64EC would add ABI complexity without benefit.",
            "evidenceIds": ["ev-dep-torch"],
            "guidanceIds": ["arm64ec-overview-01"],
        }
    ],
    "workItems": [
        {
            "id": "wi-add-arm64-build",
            "sequence": 1,
            "priority": "P0",
            "title": "Add ARM64 build target and CI job",
            "objective": "Produce an ARM64 build artifact and add an ARM64 job to the CI matrix.",
            "agentOrSkill": "automated-migration/build-configuration",
            "inputs": ["repositoryPath", "assessmentId"],
            "expectedOutputs": ["build-config-patch", "ci-config-patch"],
            "dependencies": [],
            "evidenceIds": ["ev-build-001"],
            "guidanceIds": ["add-arm-support-01"],
            "acceptanceTests": [
                {
                    "id": "at-arm64-artifact",
                    "description": "CI matrix produces an ARM64 build artifact.",
                    "expectedOutcome": "Artifact 'ComfyUI-arm64.zip' present in CI outputs.",
                    "evidenceIds": [],
                    "guidanceIds": [],
                }
            ],
            "approvalRequired": True,
            "estimatedEffort": "medium",
            "risk": "medium",
        }
    ],
    "missingSkills": [],
    "validationPlan": {
        "targetDevices": ["snapdragon-x-series", "arm64-vm"],
        "buildChecks": [
            {
                "id": "vc-arm64-build",
                "description": "ARM64 build succeeds on Windows on Arm CI runner.",
                "expectedOutcome": "Build returns exit code 0 with an ARM64 artifact.",
                "evidenceIds": [],
                "guidanceIds": [],
            }
        ],
        "functionalChecks": [],
        "reliabilityChecks": [],
        "performanceChecks": [],
        "powerChecks": [],
        "offlineChecks": [],
        "accessibilityChecks": [],
        "windowsExperienceChecks": [],
    },
    "risks": [
        {
            "id": "rk-native-dep-drift",
            "description": "Future dependency updates may introduce non-ARM64 native components.",
            "severity": "medium",
            "mitigation": "Add a pre-merge scanner check for architectureStatus of new dependencies.",
            "evidenceIds": ["ev-dep-torch"],
            "guidanceIds": [],
        }
    ],
    "unknowns": [
        {
            "id": "uk-perf-baseline",
            "description": "No prior ARM64 performance baseline exists for the diffusion pipeline.",
            "requiredSkill": "validation/performance-baseline",
            "evidenceIds": ["ev-build-001"],
        }
    ],
    "requiredApprovals": [
        {
            "approvalId": "ap-add-arm64-build",
            "summary": "Approve adding an ARM64 build target and CI job to the repository.",
            "workItemIds": ["wi-add-arm64-build"],
        }
    ],
    "reusableOutputs": [
        {
            "name": "arm64-build-matrix-template",
            "kind": "template",
            "description": "Reusable ARM64 CI matrix snippet applicable to Python + native-extension repos.",
            "artifactRef": "templates/arm64-build-matrix.yml",
            "evidenceIds": [],
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
            "unknown recommendedPath rejected",
            lambda s: _mutate(s, ["recommendedPath"], "linux-arm64"),
        ),
        (
            "unknown confidence rejected",
            lambda s: _mutate(s, ["confidence"], "very-high"),
        ),
        (
            "scoreDigest wrong length rejected",
            lambda s: _mutate(s, ["scoreDigest"], "ab" * 20),
        ),
        (
            "scoreDigest uppercase rejected",
            lambda s: _mutate(s, ["scoreDigest"], "B" * 64),
        ),
        (
            "corpusVersion wrong format rejected",
            lambda s: _mutate(s, ["corpusVersion"], "v1"),
        ),
        (
            "workItem approvalRequired = false rejected",
            lambda s: _mutate(s, ["workItems", 0, "approvalRequired"], False),
        ),
        (
            "workItem missing approvalRequired rejected",
            lambda s: _delete(s, ["workItems", 0, "approvalRequired"]),
        ),
        (
            "workItem invalid id prefix rejected",
            lambda s: _mutate(s, ["workItems", 0, "id"], "task-1"),
        ),
        (
            "workItem unknown priority rejected",
            lambda s: _mutate(s, ["workItems", 0, "priority"], "P3"),
        ),
        (
            "workItem risk unknown rejected",
            lambda s: _mutate(s, ["workItems", 0, "risk"], "catastrophic"),
        ),
        (
            "workItem effort unknown-value rejected",
            lambda s: _mutate(s, ["workItems", 0, "estimatedEffort"], "huge"),
        ),
        (
            "workItem dependency malformed rejected",
            lambda s: _mutate(s, ["workItems", 0, "dependencies"], ["task-1"]),
        ),
        (
            "workItem empty acceptanceTests rejected",
            lambda s: _mutate(s, ["workItems", 0, "acceptanceTests"], []),
        ),
        (
            "alternatives empty rejected",
            lambda s: _mutate(s, ["alternatives"], []),
        ),
        (
            "alternative disposition unknown rejected",
            lambda s: _mutate(
                s, ["alternatives", 0, "disposition"], "maybe"
            ),
        ),
        (
            "guidanceId wrong shape rejected",
            lambda s: _mutate(
                s, ["inferences", 0, "guidanceIds"], ["Arm64EC"]
            ),
        ),
        (
            "reusableOutput unknown kind rejected",
            lambda s: _mutate(s, ["reusableOutputs", 0, "kind"], "widget"),
        ),
        (
            "extra top-level property rejected",
            lambda s: _mutate(s, ["hackerField"], "should-fail"),
        ),
        (
            "oversize executiveSummary rejected",
            lambda s: _mutate(s, ["executiveSummary"], "x" * 3000),
        ),
        (
            "missing modelProvenance rejected",
            lambda s: _delete(s, ["modelProvenance"]),
        ),
        (
            "validationPlan targetDevices empty rejected",
            lambda s: _mutate(
                s, ["validationPlan", "targetDevices"], []
            ),
        ),
        (
            "validationPlan targetDevices unknown rejected",
            lambda s: _mutate(
                s, ["validationPlan", "targetDevices"], ["linux-arm64"]
            ),
        ),
        (
            "malformed generatedAt rejected",
            lambda s: _mutate(s, ["generatedAt"], "tomorrow"),
        ),
        (
            "approval workItemIds empty rejected",
            lambda s: _mutate(
                s, ["requiredApprovals", 0, "workItemIds"], []
            ),
        ),
    ]

    passed = sum(_run_negative(schema, label, m) for label, m in checks)
    print(f"\n{passed}/{len(checks)} negative checks confirmed rejection")
    return 0 if passed == len(checks) else 2


if __name__ == "__main__":
    raise SystemExit(main())
