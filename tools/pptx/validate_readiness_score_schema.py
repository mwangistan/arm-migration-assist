"""Validate ReadinessScoreV1.schema.json and prove the hardening rules bite.

Runs three passes:
    1. Meta-validate the schema against Draft 2020-12.
    2. Validate a minimal well-formed sample score.
    3. Feed intentionally-broken samples and require them to be rejected.

Run: python tools/pptx/validate_readiness_score_schema.py
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
    / "ReadinessScoreV1.schema.json"
)


def _dim(key, weight, raw, contrib, conf, rationale, evidence):
    return {
        "dimensionKey": key,
        "weightPct": weight,
        "rawScore": raw,
        "weightedContribution": contrib,
        "deductions": [],
        "confidence": conf,
        "rationaleCodes": rationale,
        "evidenceIds": evidence,
    }


VALID_SAMPLE = {
    "schemaVersion": "1.0",
    "assessmentId": "asm-2026-09-15-comfyui-001",
    "generatedAt": "2026-09-15T18:30:00Z",
    "producer": {
        "name": "arm-migration-assist-scorer",
        "version": "0.1.0",
        "ruleset": "score-ruleset-2026-09-15",
    },
    "overallScore": 78,
    "uncappedScore": 78,
    "band": "moderate-migration",
    "confidence": "medium",
    "confidenceScore": 0.62,
    "provisional": False,
    "provisionalReasons": [],
    "dimensions": [
        _dim("dependency-compatibility", 30, 85, 25.5, 0.7, ["DEP-REPLACEMENT-AVAILABLE-torch"], ["ev-dep-torch"]),
        _dim("code-compatibility", 25, 70, 17.5, 0.6, ["CODE-HIGH-ARM-CODE-SIMD-01"], ["ev-code-simd-001"]),
        _dim("build-and-ci-readiness", 20, 60, 12.0, 0.7, ["BUILD-NO-ARM64"], ["ev-build-001"]),
        _dim("runtime-and-validation-evidence", 15, 80, 12.0, 0.5, ["RUN-COVERAGE-OK"], ["ev-build-001"]),
        _dim("windows-experience-and-deployment", 10, 55, 5.5, 0.4, ["WIN-UI-web"], ["ev-build-001"]),
    ],
    "capsApplied": [],
    "majorBlockers": [
        {
            "blockerId": "bl-missing-arm64-build",
            "category": "build",
            "description": "No ARM64 build target detected in project files or CI.",
            "evidenceIds": ["ev-build-001"],
        }
    ],
    "rationaleCodes": [
        "DEP-REPLACEMENT-AVAILABLE-torch",
        "CODE-HIGH-ARM-CODE-SIMD-01",
        "BUILD-NO-ARM64",
        "RUN-COVERAGE-OK",
        "WIN-UI-web",
    ],
    "evidenceIds": ["ev-dep-torch", "ev-code-simd-001", "ev-build-001"],
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
            "overallScore > 100 rejected",
            lambda s: _mutate(s, ["overallScore"], 105),
        ),
        (
            "overallScore negative rejected",
            lambda s: _mutate(s, ["overallScore"], -1),
        ),
        (
            "band unknown rejected",
            lambda s: _mutate(s, ["band"], "not-a-band"),
        ),
        (
            "confidence unknown rejected",
            lambda s: _mutate(s, ["confidence"], "very-high"),
        ),
        (
            "confidenceScore > 1 rejected",
            lambda s: _mutate(s, ["confidenceScore"], 1.5),
        ),
        (
            "fewer than 5 dimensions rejected",
            lambda s: _mutate(s, ["dimensions"], s["dimensions"][:4]),
        ),
        (
            "more than 5 dimensions rejected",
            lambda s: _mutate(
                s,
                ["dimensions"],
                s["dimensions"] + [_dim("dependency-compatibility", 0, 0, 0, 0, [], [])],
            ),
        ),
        (
            "dimensionKey unknown rejected",
            lambda s: _mutate(s, ["dimensions", 0, "dimensionKey"], "cost"),
        ),
        (
            "weightPct > 100 rejected",
            lambda s: _mutate(s, ["dimensions", 0, "weightPct"], 150),
        ),
        (
            "rawScore > 100 rejected",
            lambda s: _mutate(s, ["dimensions", 0, "rawScore"], 200),
        ),
        (
            "dimension confidence > 1 rejected",
            lambda s: _mutate(s, ["dimensions", 0, "confidence"], 2),
        ),
        (
            "provisionalReason unknown rejected",
            lambda s: _mutate(s, ["provisionalReasons"], ["dunno"]),
        ),
        (
            "capId unknown rejected",
            lambda s: _mutate(
                s,
                ["capsApplied"],
                [
                    {
                        "capId": "some-other-cap",
                        "ceiling": 50,
                        "description": "unused",
                        "triggeredBy": ["ev-build-001"],
                    }
                ],
            ),
        ),
        (
            "cap missing triggeredBy rejected",
            lambda s: _mutate(
                s,
                ["capsApplied"],
                [
                    {
                        "capId": "no-arm64-or-arm64ec-target-le-60",
                        "ceiling": 60,
                        "description": "no ARM64 target detected",
                        "triggeredBy": [],
                    }
                ],
            ),
        ),
        (
            "deduction magnitude negative rejected",
            lambda s: _mutate(
                s,
                ["dimensions", 0, "deductions"],
                [
                    {
                        "code": "DEP-BLOCKED-torch",
                        "magnitude": -5,
                        "description": "blocked",
                        "evidenceIds": ["ev-dep-torch"],
                    }
                ],
            ),
        ),
        (
            "rationaleCode wrong shape rejected",
            lambda s: _mutate(
                s, ["rationaleCodes"], ["dep-blocked-torch"]
            ),
        ),
        (
            "majorBlocker id malformed rejected",
            lambda s: _mutate(
                s, ["majorBlockers", 0, "blockerId"], "no-prefix"
            ),
        ),
        (
            "majorBlocker category unknown rejected",
            lambda s: _mutate(s, ["majorBlockers", 0, "category"], "cost"),
        ),
        (
            "majorBlocker empty evidenceIds rejected",
            lambda s: _mutate(s, ["majorBlockers", 0, "evidenceIds"], []),
        ),
        (
            "malformed generatedAt rejected",
            lambda s: _mutate(s, ["generatedAt"], "yesterday"),
        ),
        (
            "missing producer rejected",
            lambda s: _delete(s, ["producer"]),
        ),
        (
            "producer missing ruleset rejected",
            lambda s: _delete(s, ["producer", "ruleset"]),
        ),
        (
            "extra top-level property rejected",
            lambda s: _mutate(s, ["hackerField"], "should-fail"),
        ),
    ]

    passed = sum(_run_negative(schema, label, m) for label, m in checks)
    print(f"\n{passed}/{len(checks)} negative checks confirmed rejection")
    return 0 if passed == len(checks) else 2


if __name__ == "__main__":
    raise SystemExit(main())
