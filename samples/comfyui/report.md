# ComfyUI migration report

- Repository: https://github.com/comfyanonymous/ComfyUI
- Commit: `387f98aa2822f684b8597959a52a467d88cc4806`
- Branch: `master`
- Run: `baeca60e2e19484abe72925257ce3968`
- Generated: 2026-09-17T15:32:04.6096043+00:00

## Verdict

**14/100 - BlockedOrMajorRedesign**

Recommended path: **native-arm64** 
Confidence: **low**

## Executive summary

Deterministic band 'blocked-or-major-redesign' at overall 14/100 (uncapped 14). Cap(s) applied: no-arm64-or-arm64ec-target-le-60. Assessment flagged provisional (dependency-resolution-low, dimension-missing-evidence).

## Readiness dimensions

| Dimension | Score | Weight | Contribution |
|---|---:|---:|---:|
| DependencyCompatibility | 0 | 33% | 0 |
| CodeCompatibility | 0 | 28% | 0 |
| BuildAndCiReadiness | 15 | 22% | 3.3 |
| RuntimeAndValidationEvidence | 60 | 17% | 10.2 |
| WindowsExperienceAndDeployment | 22 | 0% | 0 |

## Migration work

### 1. Add an ARM64 or Arm64EC build target.

- Priority: P0
- Skill: `build/add-arm64-target`
- Approval required: True
- Evidence: build-a411db87a55ff51b5b35

Add an ARM64 or Arm64EC build target. Produce the concrete change and validate on ARM64.

Acceptance criteria:
- Change builds and integrates with existing pipelines. Expected: Green build on ARM64.
- Functional smoke test passes on ARM64. Expected: No crash or regression against the x64 baseline.

### 2. Add an ARM64 CI job.

- Priority: P0
- Skill: `pipeline/github-actions-arm64-job`
- Approval required: True
- Evidence: build-a411db87a55ff51b5b35

Add an ARM64 CI job. Produce the concrete change and validate on ARM64.

Acceptance criteria:
- Change builds and integrates with existing pipelines. Expected: Green build on ARM64.
- Functional smoke test passes on ARM64. Expected: No crash or regression against the x64 baseline.

## Alternatives considered

- **native-arm64** (viable): Deterministic score band 'blocked-or-major-redesign' (overall 14) drives this recommendation.

## Risks and unknowns

- **high:** Build does not produce an ARM64 or Arm64EC target. Mitigation: Resolve the linked evidence through an approval-gated work item and rerun ARM64 validation.
- **Unknown:** ARM64 availability could not be established from repository evidence for 39 declared dependency or dependencies.
- **Unknown:** Meaningful offline capability could not be established from static repository signals.
- **Unknown:** Accessibility coverage could not be established from static repository signals.
- **Unknown:** 5 tracked file(s) were not scanned because they were unsafe, missing, or exceeded scan limits.

## Validation plan

Target devices: arm64-vm
- **buildChecks:** Build the planned target in Release configuration for ARM64. Expected: The ARM64 build completes without errors.
- **functionalChecks:** Run the repository's primary functional smoke path on ARM64. Expected: The ARM64 result matches the established x64 baseline.

## Capability and approval gates

- **Missing skill `build/add-arm64-target`:** Migration capability required for 'Add an ARM64 or Arm64EC build target.'
- **Approval required:** Approve review-only migration changes before patch generation. (wi-build-add-an-arm64-or-arm64ec-build-target, wi-build-add-an-arm64-ci-job)

## Appendix

### Plan JSON
```json
{
  "schemaVersion": "1.0",
  "assessmentId": "assessment-b5a29c9cc32e432cc9338f9b",
  "planId": "plan-assessment-b5a29c9cc32e432cc9338f9b",
  "generatedAt": "2026-09-17T15:32:04.6096043\u002B00:00",
  "modelProvenance": {
    "provider": "fake",
    "name": "fake-planner",
    "version": "0.1.0"
  },
  "scoreDigest": "138fa8ba73d726ff4e5c6f0f6dcc8d42e30a8fff6e82a1072e93062a8a34c3dd",
  "corpusVersion": "2026-09-15.1",
  "recommendedPath": "native-arm64",
  "confidence": "low",
  "executiveSummary": "Deterministic band \u0027blocked-or-major-redesign\u0027 at overall 14/100 (uncapped 14). Cap(s) applied: no-arm64-or-arm64ec-target-le-60. Assessment flagged provisional (dependency-resolution-low, dimension-missing-evidence).",
  "scoreInterpretation": "Dimension breakdown (dependency-compatibility: raw 0 \u00D7 33% = 0; code-compatibility: raw 0 \u00D7 28% = 0; build-and-ci-readiness: raw 15 \u00D7 22% = 3.3; runtime-and-validation-evidence: raw 60 \u00D7 17% = 10.2; windows-experience-and-deployment: raw 22 \u00D7 0% = 0). Evidence completeness 0.60 (medium).",
  "facts": [
    {
      "statement": "Deterministic scorer observed 67 evidence record(s) across five dimensions.",
      "evidenceIds": [
        "build-a411db87a55ff51b5b35",
        "code-01f32f628a3a8c71316e",
        "code-07d36ddd828c6778134a",
        "code-0f09bccdb35f2acfa408",
        "code-173d76fa57352bb22a57"
      ],
      "guidanceIds": []
    }
  ],
  "inferences": [],
  "alternatives": [
    {
      "path": "native-arm64",
      "disposition": "viable",
      "rationale": "Deterministic score band \u0027blocked-or-major-redesign\u0027 (overall 14) drives this recommendation.",
      "evidenceIds": [
        "build-a411db87a55ff51b5b35",
        "code-01f32f628a3a8c71316e",
        "code-07d36ddd828c6778134a",
        "code-0f09bccdb35f2acfa408",
        "code-173d76fa57352bb22a57"
      ],
      "guidanceIds": []
    }
  ],
  "workItems": [
    {
      "id": "wi-build-add-an-arm64-or-arm64ec-build-target",
      "sequence": 1,
      "priority": "P0",
      "title": "Add an ARM64 or Arm64EC build target.",
      "objective": "Add an ARM64 or Arm64EC build target. Produce the concrete change and validate on ARM64.",
      "agentOrSkill": "build/add-arm64-target",
      "inputs": [],
      "expectedOutputs": [
        "patch"
      ],
      "dependencies": [],
      "evidenceIds": [
        "build-a411db87a55ff51b5b35"
      ],
      "guidanceIds": [],
      "acceptanceTests": [
        {
          "id": "at-build-2-builds",
          "description": "Change builds and integrates with existing pipelines.",
          "expectedOutcome": "Green build on ARM64."
        },
        {
          "id": "at-build-2-functional",
          "description": "Functional smoke test passes on ARM64.",
          "expectedOutcome": "No crash or regression against the x64 baseline."
        }
      ],
      "approvalRequired": true,
      "estimatedEffort": "medium",
      "risk": "low"
    },
    {
      "id": "wi-build-add-an-arm64-ci-job",
      "sequence": 2,
      "priority": "P0",
      "title": "Add an ARM64 CI job.",
      "objective": "Add an ARM64 CI job. Produce the concrete change and validate on ARM64.",
      "agentOrSkill": "pipeline/github-actions-arm64-job",
      "inputs": [],
      "expectedOutputs": [
        "patch"
      ],
      "dependencies": [
        "wi-build-add-an-arm64-or-arm64ec-build-target"
      ],
      "evidenceIds": [
        "build-a411db87a55ff51b5b35"
      ],
      "guidanceIds": [],
      "acceptanceTests": [
        {
          "id": "at-build-3-builds",
          "description": "Change builds and integrates with existing pipelines.",
          "expectedOutcome": "Green build on ARM64."
        },
        {
          "id": "at-build-3-functional",
          "description": "Functional smoke test passes on ARM64.",
          "expectedOutcome": "No crash or regression against the x64 baseline."
        }
      ],
      "approvalRequired": true,
      "estimatedEffort": "medium",
      "risk": "low"
    }
  ],
  "missingSkills": [
    {
      "proposedName": "build/add-arm64-target",
      "purpose": "Migration capability required for \u0027Add an ARM64 or Arm64EC build target.\u0027",
      "requiredInputs": [],
      "expectedOutputs": [
        "patch"
      ],
      "justification": "The required migration generator is not present in the assessment skill catalog.",
      "evidenceIds": [
        "build-a411db87a55ff51b5b35"
      ],
      "writeAccess": true
    }
  ],
  "validationPlan": {
    "targetDevices": [
      "arm64-vm"
    ],
    "buildChecks": [
      {
        "id": "vc-arm64-release-build",
        "description": "Build the planned target in Release configuration for ARM64.",
        "expectedOutcome": "The ARM64 build completes without errors."
      }
    ],
    "functionalChecks": [
      {
        "id": "vc-arm64-functional-smoke",
        "description": "Run the repository\u0027s primary functional smoke path on ARM64.",
        "expectedOutcome": "The ARM64 result matches the established x64 baseline."
      }
    ],
    "reliabilityChecks": [],
    "performanceChecks": [],
    "powerChecks": [],
    "offlineChecks": [],
    "accessibilityChecks": [],
    "windowsExperienceChecks": []
  },
  "risks": [
    {
      "id": "rk-blocker-1",
      "description": "Build does not produce an ARM64 or Arm64EC target.",
      "severity": "high",
      "mitigation": "Resolve the linked evidence through an approval-gated work item and rerun ARM64 validation.",
      "evidenceIds": [
        "build-a411db87a55ff51b5b35"
      ],
      "guidanceIds": []
    }
  ],
  "unknowns": [
    {
      "id": "uk-assessment-1",
      "description": "ARM64 availability could not be established from repository evidence for 39 declared dependency or dependencies.",
      "evidenceIds": [
        "dependency-843725321ce800b7823d",
        "dependency-fc3843ea62fb3fc3910f",
        "dependency-bf40c841f26a3c63e523",
        "dependency-7875e31caf01e0572bb3",
        "dependency-e22fb8cd8caa1567977b",
        "dependency-5efcd0aec421efcfafcd",
        "dependency-3ec1150f155b8fd5051a",
        "dependency-fc08762b6dd930acda43",
        "dependency-86bb1c22b8b9e6529c7a",
        "dependency-0209a8d233443e4ad955",
        "dependency-651f6c9fa1f5f9f6799c",
        "dependency-e6c283166f5ddec2238a",
        "dependency-c199703bd4a2c639ef88",
        "dependency-9f5278f94de0f2330fdb",
        "dependency-5cc12dab4268dcbe3700",
        "dependency-8b2625ff7ee1d1d30a53",
        "dependency-cd069dc8547bbbb1c498",
        "dependency-805f2565a46f25f6dd7a",
        "dependency-b1e184a7144f74db052a",
        "dependency-7f9bd7f2a7df7f5a7391"
      ]
    },
    {
      "id": "uk-assessment-2",
      "description": "Meaningful offline capability could not be established from static repository signals.",
      "evidenceIds": []
    },
    {
      "id": "uk-assessment-3",
      "description": "Accessibility coverage could not be established from static repository signals.",
      "evidenceIds": []
    },
    {
      "id": "uk-assessment-4",
      "description": "5 tracked file(s) were not scanned because they were unsafe, missing, or exceeded scan limits.",
      "evidenceIds": []
    }
  ],
  "requiredApprovals": [
    {
      "approvalId": "ap-migration-work-1",
      "summary": "Approve review-only migration changes before patch generation.",
      "workItemIds": [
        "wi-build-add-an-arm64-or-arm64ec-build-target",
        "wi-build-add-an-arm64-ci-job"
      ]
    }
  ],
  "reusableOutputs": []
}
```

### Score JSON
```json
{
  "schemaVersion": "1.0",
  "assessmentId": "assessment-b5a29c9cc32e432cc9338f9b",
  "generatedAt": "2026-09-17T15:23:48.1269469+00:00",
  "producer": {
    "name": "arm-migration-assist-scorer",
    "version": "0.2.0",
    "ruleset": "scoring-v2"
  },
  "overallScore": 14,
  "uncappedScore": 14,
  "band": "blocked-or-major-redesign",
  "evidenceCompleteness": "medium",
  "evidenceCompletenessScore": 0.6,
  "provisional": true,
  "provisionalReasons": [
    "dependency-resolution-low",
    "dimension-missing-evidence"
  ],
  "dimensions": [
    {
      "dimensionKey": "dependency-compatibility",
      "weightPct": 33,
      "rawScore": 0,
      "weightedContribution": 0,
      "deductions": [
        {
          "code": "DEP-UNKNOWN-REQUIRED",
          "magnitude": 8,
          "description": "Dependency \u0027comfyui-workflow-templates\u0027 is unknown (required).",
          "evidenceIds": [
            "dependency-0209a8d233443e4ad955"
          ]
        },
        {
          "code": "DEP-UNKNOWN-REQUIRED",
          "magnitude": 8,
          "description": "Dependency \u0027spandrel\u0027 is unknown (required).",
          "evidenceIds": [
            "dependency-1fec3d082a9a15b05c0a"
          ]
        },
        {
          "code": "DEP-UNKNOWN-REQUIRED",
          "magnitude": 8,
          "description": "Dependency \u0027tokenizers\u0027 is unknown (required).",
          "evidenceIds": [
            "dependency-270709d5ed238556cc99"
          ]
        },
        {
          "code": "DEP-UNKNOWN-REQUIRED",
          "magnitude": 8,
          "description": "Dependency \u0027sentencepiece\u0027 is unknown (required).",
          "evidenceIds": [
            "dependency-2f3617105f3296a059d6"
          ]
        },
        {
          "code": "DEP-UNKNOWN-REQUIRED",
          "magnitude": 8,
          "description": "Dependency \u0027scipy\u0027 is unknown (required).",
          "evidenceIds": [
            "dependency-3442d4117305f1f58da4"
          ]
        },
        {
          "code": "DEP-UNKNOWN-REQUIRED",
          "magnitude": 8,
          "description": "Dependency \u0027comfy-kitchen\u0027 is unknown (required).",
          "evidenceIds": [
            "dependency-3ec1150f155b8fd5051a"
          ]
        },
        {
          "code": "DEP-UNKNOWN-REQUIRED",
          "magnitude": 8,
          "description": "Dependency \u0027pytest-aiohttp\u0027 is unknown (required).",
          "evidenceIds": [
            "dependency-44cd82dbc52e46a6a6ad"
          ]
        },
        {
          "code": "DEP-UNKNOWN-REQUIRED",
          "magnitude": 8,
          "description": "Dependency \u0027SQLAlchemy\u0027 is unknown (required).",
          "evidenceIds": [
            "dependency-4c9ef52d52fba68c1646"
          ]
        },
        {
          "code": "DEP-UNKNOWN-REQUIRED",
          "magnitude": 8,
          "description": "Dependency \u0027tqdm\u0027 is unknown (required).",
          "evidenceIds": [
            "dependency-595a70b1a22872d2ae5a"
          ]
        },
        {
          "code": "DEP-UNKNOWN-REQUIRED",
          "magnitude": 8,
          "description": "Dependency \u0027Pillow\u0027 is unknown (required).",
          "evidenceIds": [
            "dependency-5cc12dab4268dcbe3700"
          ]
        },
        {
          "code": "DEP-UNKNOWN-REQUIRED",
          "magnitude": 8,
          "description": "Dependency \u0027comfy-angle\u0027 is unknown (required).",
          "evidenceIds": [
            "dependency-5efcd0aec421efcfafcd"
          ]
        },
        {
          "code": "DEP-UNKNOWN-REQUIRED",
          "magnitude": 8,
          "description": "Dependency \u0027torchaudio\u0027 is unknown (required).",
          "evidenceIds": [
            "dependency-649e8fa0e536ce7f332f"
          ]
        },
        {
          "code": "DEP-UNKNOWN-REQUIRED",
          "magnitude": 8,
          "description": "Dependency \u0027einops\u0027 is unknown (required).",
          "evidenceIds": [
            "dependency-651f6c9fa1f5f9f6799c"
          ]
        },
        {
          "code": "DEP-UNKNOWN-REQUIRED",
          "magnitude": 8,
          "description": "Dependency \u0027transformers\u0027 is unknown (required).",
          "evidenceIds": [
            "dependency-6b5cc84d468bee0568ce"
          ]
        },
        {
          "code": "DEP-UNKNOWN-REQUIRED",
          "magnitude": 8,
          "description": "Dependency \u0027blake3\u0027 is unknown (required).",
          "evidenceIds": [
            "dependency-7875e31caf01e0572bb3"
          ]
        },
        {
          "code": "DEP-UNKNOWN-REQUIRED",
          "magnitude": 8,
          "description": "Dependency \u0027pytest\u0027 is unknown (required).",
          "evidenceIds": [
            "dependency-7f9bd7f2a7df7f5a7391"
          ]
        },
        {
          "code": "DEP-UNKNOWN-REQUIRED",
          "magnitude": 8,
          "description": "Dependency \u0027pydantic-settings\u0027 is unknown (required).",
          "evidenceIds": [
            "dependency-805f2565a46f25f6dd7a"
          ]
        },
        {
          "code": "DEP-UNKNOWN-REQUIRED",
          "magnitude": 8,
          "description": "Dependency \u0027aiohttp\u0027 is unknown (required).",
          "evidenceIds": [
            "dependency-843725321ce800b7823d"
          ]
        },
        {
          "code": "DEP-UNKNOWN-REQUIRED",
          "magnitude": 8,
          "description": "Dependency \u0027comfyui-frontend-package\u0027 is unknown (required).",
          "evidenceIds": [
            "dependency-86bb1c22b8b9e6529c7a"
          ]
        },
        {
          "code": "DEP-UNKNOWN-REQUIRED",
          "magnitude": 8,
          "description": "Dependency \u0027psutil\u0027 is unknown (required).",
          "evidenceIds": [
            "dependency-8b2625ff7ee1d1d30a53"
          ]
        },
        {
          "code": "DEP-UNKNOWN-REQUIRED",
          "magnitude": 8,
          "description": "Dependency \u0027numpy\u0027 is unknown (required).",
          "evidenceIds": [
            "dependency-9f5278f94de0f2330fdb"
          ]
        },
        {
          "code": "DEP-UNKNOWN-REQUIRED",
          "magnitude": 8,
          "description": "Dependency \u0027websocket-client\u0027 is unknown (required).",
          "evidenceIds": [
            "dependency-a33bde44e945be467683"
          ]
        },
        {
          "code": "DEP-UNKNOWN-REQUIRED",
          "magnitude": 8,
          "description": "Dependency \u0027PyOpenGL\u0027 is unknown (required).",
          "evidenceIds": [
            "dependency-b1e184a7144f74db052a"
          ]
        },
        {
          "code": "DEP-UNKNOWN-REQUIRED",
          "magnitude": 8,
          "description": "Dependency \u0027torchvision\u0027 is unknown (required).",
          "evidenceIds": [
            "dependency-b35ad527ad90f7240528"
          ]
        },
        {
          "code": "DEP-UNKNOWN-REQUIRED",
          "magnitude": 8,
          "description": "Dependency \u0027simpleeval\u0027 is unknown (required).",
          "evidenceIds": [
            "dependency-bb4afeb038eec462365c"
          ]
        },
        {
          "code": "DEP-UNKNOWN-REQUIRED",
          "magnitude": 8,
          "description": "Dependency \u0027av\u0027 is unknown (required).",
          "evidenceIds": [
            "dependency-bf40c841f26a3c63e523"
          ]
        },
        {
          "code": "DEP-UNKNOWN-REQUIRED",
          "magnitude": 8,
          "description": "Dependency \u0027kornia\u0027 is unknown (required).",
          "evidenceIds": [
            "dependency-c199703bd4a2c639ef88"
          ]
        },
        {
          "code": "DEP-UNKNOWN-REQUIRED",
          "magnitude": 8,
          "description": "Dependency \u0027requests\u0027 is unknown (required).",
          "evidenceIds": [
            "dependency-c2fbfd514f3ea1aed419"
          ]
        },
        {
          "code": "DEP-UNKNOWN-REQUIRED",
          "magnitude": 8,
          "description": "Dependency \u0027pydantic\u0027 is unknown (required).",
          "evidenceIds": [
            "dependency-cd069dc8547bbbb1c498"
          ]
        },
        {
          "code": "DEP-UNKNOWN-REQUIRED",
          "magnitude": 8,
          "description": "Dependency \u0027yarl\u0027 is unknown (required).",
          "evidenceIds": [
            "dependency-d01eafc3fc88063cc987"
          ]
        },
        {
          "code": "DEP-UNKNOWN-REQUIRED",
          "magnitude": 8,
          "description": "Dependency \u0027pyyaml\u0027 is unknown (required).",
          "evidenceIds": [
            "dependency-dafb551d9a27fb237c90"
          ]
        },
        {
          "code": "DEP-UNKNOWN-REQUIRED",
          "magnitude": 8,
          "description": "Dependency \u0027comfy-aimdo\u0027 is unknown (required).",
          "evidenceIds": [
            "dependency-e22fb8cd8caa1567977b"
          ]
        },
        {
          "code": "DEP-UNKNOWN-REQUIRED",
          "magnitude": 8,
          "description": "Dependency \u0027filelock\u0027 is unknown (required).",
          "evidenceIds": [
            "dependency-e6c283166f5ddec2238a"
          ]
        },
        {
          "code": "DEP-UNKNOWN-REQUIRED",
          "magnitude": 8,
          "description": "Dependency \u0027safetensors\u0027 is unknown (required).",
          "evidenceIds": [
            "dependency-fa140c3b2b779fd231dc"
          ]
        },
        {
          "code": "DEP-UNKNOWN-REQUIRED",
          "magnitude": 8,
          "description": "Dependency \u0027comfyui-embedded-docs\u0027 is unknown (required).",
          "evidenceIds": [
            "dependency-fc08762b6dd930acda43"
          ]
        },
        {
          "code": "DEP-UNKNOWN-REQUIRED",
          "magnitude": 8,
          "description": "Dependency \u0027alembic\u0027 is unknown (required).",
          "evidenceIds": [
            "dependency-fc3843ea62fb3fc3910f"
          ]
        },
        {
          "code": "DEP-UNKNOWN-REQUIRED",
          "magnitude": 8,
          "description": "Dependency \u0027torchsde\u0027 is unknown (required).",
          "evidenceIds": [
            "dependency-fdfee7f25ce88bef9a44"
          ]
        },
        {
          "code": "DEP-UNKNOWN-REQUIRED",
          "magnitude": 8,
          "description": "Dependency \u0027pytest-asyncio\u0027 is unknown (required).",
          "evidenceIds": [
            "dependency-fea91d90d248f42f4cf6"
          ]
        },
        {
          "code": "DEP-UNKNOWN-REQUIRED",
          "magnitude": 8,
          "description": "Dependency \u0027torch\u0027 is unknown (required).",
          "evidenceIds": [
            "dependency-ff049b7c4a994e34bedb"
          ]
        }
      ],
      "confidence": 0.4,
      "rationaleCodes": [
        "DEP-UNKNOWN-REQUIRED"
      ],
      "evidenceIds": [
        "dependency-0209a8d233443e4ad955",
        "dependency-1fec3d082a9a15b05c0a",
        "dependency-270709d5ed238556cc99",
        "dependency-2f3617105f3296a059d6",
        "dependency-3442d4117305f1f58da4",
        "dependency-3ec1150f155b8fd5051a",
        "dependency-44cd82dbc52e46a6a6ad",
        "dependency-4c9ef52d52fba68c1646",
        "dependency-595a70b1a22872d2ae5a",
        "dependency-5cc12dab4268dcbe3700",
        "dependency-5efcd0aec421efcfafcd",
        "dependency-649e8fa0e536ce7f332f",
        "dependency-651f6c9fa1f5f9f6799c",
        "dependency-6b5cc84d468bee0568ce",
        "dependency-7875e31caf01e0572bb3",
        "dependency-7f9bd7f2a7df7f5a7391",
        "dependency-805f2565a46f25f6dd7a",
        "dependency-843725321ce800b7823d",
        "dependency-86bb1c22b8b9e6529c7a",
        "dependency-8b2625ff7ee1d1d30a53",
        "dependency-9f5278f94de0f2330fdb",
        "dependency-a33bde44e945be467683",
        "dependency-b1e184a7144f74db052a",
        "dependency-b35ad527ad90f7240528",
        "dependency-bb4afeb038eec462365c",
        "dependency-bf40c841f26a3c63e523",
        "dependency-c199703bd4a2c639ef88",
        "dependency-c2fbfd514f3ea1aed419",
        "dependency-cd069dc8547bbbb1c498",
        "dependency-d01eafc3fc88063cc987",
        "dependency-dafb551d9a27fb237c90",
        "dependency-e22fb8cd8caa1567977b",
        "dependency-e6c283166f5ddec2238a",
        "dependency-fa140c3b2b779fd231dc",
        "dependency-fc08762b6dd930acda43",
        "dependency-fc3843ea62fb3fc3910f",
        "dependency-fdfee7f25ce88bef9a44",
        "dependency-fea91d90d248f42f4cf6",
        "dependency-ff049b7c4a994e34bedb"
      ]
    },
    {
      "dimensionKey": "code-compatibility",
      "weightPct": 28,
      "rawScore": 0,
      "weightedContribution": 0,
      "deductions": [
        {
          "code": "CODE-HIGH-ARM-CODE-SIMD-01",
          "magnitude": 11,
          "description": "high code finding \u0027ARM-CODE-SIMD-01\u0027: An x86 SIMD intrinsic or header is referenced.",
          "evidenceIds": [
            "code-01f32f628a3a8c71316e"
          ]
        },
        {
          "code": "CODE-HIGH-ARM-CODE-SIMD-01",
          "magnitude": 11,
          "description": "high code finding \u0027ARM-CODE-SIMD-01\u0027: An x86 SIMD intrinsic or header is referenced.",
          "evidenceIds": [
            "code-07d36ddd828c6778134a"
          ]
        },
        {
          "code": "CODE-HIGH-ARM-CODE-SIMD-01",
          "magnitude": 11,
          "description": "high code finding \u0027ARM-CODE-SIMD-01\u0027: An x86 SIMD intrinsic or header is referenced.",
          "evidenceIds": [
            "code-0f09bccdb35f2acfa408"
          ]
        },
        {
          "code": "CODE-HIGH-ARM-CODE-SIMD-01",
          "magnitude": 11,
          "description": "high code finding \u0027ARM-CODE-SIMD-01\u0027: An x86 SIMD intrinsic or header is referenced.",
          "evidenceIds": [
            "code-173d76fa57352bb22a57"
          ]
        },
        {
          "code": "CODE-HIGH-ARM-CODE-SIMD-01",
          "magnitude": 11,
          "description": "high code finding \u0027ARM-CODE-SIMD-01\u0027: An x86 SIMD intrinsic or header is referenced.",
          "evidenceIds": [
            "code-1e783980f8cd8b8f2676"
          ]
        },
        {
          "code": "CODE-HIGH-ARM-CODE-SIMD-01",
          "magnitude": 11,
          "description": "high code finding \u0027ARM-CODE-SIMD-01\u0027: An x86 SIMD intrinsic or header is referenced.",
          "evidenceIds": [
            "code-29668645e6d38fa98606"
          ]
        },
        {
          "code": "CODE-HIGH-ARM-CODE-SIMD-01",
          "magnitude": 11,
          "description": "high code finding \u0027ARM-CODE-SIMD-01\u0027: An x86 SIMD intrinsic or header is referenced.",
          "evidenceIds": [
            "code-2abae4e6ad96adbdd5c9"
          ]
        },
        {
          "code": "CODE-HIGH-ARM-CODE-SIMD-01",
          "magnitude": 11,
          "description": "high code finding \u0027ARM-CODE-SIMD-01\u0027: An x86 SIMD intrinsic or header is referenced.",
          "evidenceIds": [
            "code-3d17ceb0a9867b2fa66a"
          ]
        },
        {
          "code": "CODE-HIGH-ARM-CODE-SIMD-01",
          "magnitude": 11,
          "description": "high code finding \u0027ARM-CODE-SIMD-01\u0027: An x86 SIMD intrinsic or header is referenced.",
          "evidenceIds": [
            "code-3d30fefc33da3df61bee"
          ]
        },
        {
          "code": "CODE-HIGH-ARM-CODE-SIMD-01",
          "magnitude": 11,
          "description": "high code finding \u0027ARM-CODE-SIMD-01\u0027: An x86 SIMD intrinsic or header is referenced.",
          "evidenceIds": [
            "code-4b92e91364291d65b865"
          ]
        },
        {
          "code": "CODE-HIGH-ARM-CODE-SIMD-01",
          "magnitude": 11,
          "description": "high code finding \u0027ARM-CODE-SIMD-01\u0027: An x86 SIMD intrinsic or header is referenced.",
          "evidenceIds": [
            "code-5611ea31124e5b950eac"
          ]
        },
        {
          "code": "CODE-MEDIUM-ARM-CODE-DYNAMIC-NATIVE-01",
          "magnitude": 5,
          "description": "medium code finding \u0027ARM-CODE-DYNAMIC-NATIVE-01\u0027: A native library is loaded dynamically.",
          "evidenceIds": [
            "code-5709132da865245c8964"
          ]
        },
        {
          "code": "CODE-HIGH-ARM-CODE-SIMD-01",
          "magnitude": 11,
          "description": "high code finding \u0027ARM-CODE-SIMD-01\u0027: An x86 SIMD intrinsic or header is referenced.",
          "evidenceIds": [
            "code-5b442cb3164e6079ce5a"
          ]
        },
        {
          "code": "CODE-HIGH-ARM-CODE-SIMD-01",
          "magnitude": 11,
          "description": "high code finding \u0027ARM-CODE-SIMD-01\u0027: An x86 SIMD intrinsic or header is referenced.",
          "evidenceIds": [
            "code-7af5ae88a55536bf7078"
          ]
        },
        {
          "code": "CODE-MEDIUM-ARM-CODE-DYNAMIC-NATIVE-01",
          "magnitude": 5,
          "description": "medium code finding \u0027ARM-CODE-DYNAMIC-NATIVE-01\u0027: A native library is loaded dynamically.",
          "evidenceIds": [
            "code-845886bcc044253617f0"
          ]
        },
        {
          "code": "CODE-HIGH-ARM-CODE-SIMD-01",
          "magnitude": 11,
          "description": "high code finding \u0027ARM-CODE-SIMD-01\u0027: An x86 SIMD intrinsic or header is referenced.",
          "evidenceIds": [
            "code-951349146b164a1d31d2"
          ]
        },
        {
          "code": "CODE-HIGH-ARM-CODE-SIMD-01",
          "magnitude": 11,
          "description": "high code finding \u0027ARM-CODE-SIMD-01\u0027: An x86 SIMD intrinsic or header is referenced.",
          "evidenceIds": [
            "code-aace821121d1a00e74dd"
          ]
        },
        {
          "code": "CODE-HIGH-ARM-CODE-SIMD-01",
          "magnitude": 11,
          "description": "high code finding \u0027ARM-CODE-SIMD-01\u0027: An x86 SIMD intrinsic or header is referenced.",
          "evidenceIds": [
            "code-b617305c170832eded41"
          ]
        },
        {
          "code": "CODE-HIGH-ARM-CODE-SIMD-01",
          "magnitude": 11,
          "description": "high code finding \u0027ARM-CODE-SIMD-01\u0027: An x86 SIMD intrinsic or header is referenced.",
          "evidenceIds": [
            "code-b6d360b3d8e80e77720a"
          ]
        },
        {
          "code": "CODE-HIGH-ARM-CODE-SIMD-01",
          "magnitude": 11,
          "description": "high code finding \u0027ARM-CODE-SIMD-01\u0027: An x86 SIMD intrinsic or header is referenced.",
          "evidenceIds": [
            "code-b6f1d139ff466798f171"
          ]
        },
        {
          "code": "CODE-HIGH-ARM-CODE-SIMD-01",
          "magnitude": 11,
          "description": "high code finding \u0027ARM-CODE-SIMD-01\u0027: An x86 SIMD intrinsic or header is referenced.",
          "evidenceIds": [
            "code-bdb2ad060dfd95a200b0"
          ]
        },
        {
          "code": "CODE-HIGH-ARM-CODE-SIMD-01",
          "magnitude": 11,
          "description": "high code finding \u0027ARM-CODE-SIMD-01\u0027: An x86 SIMD intrinsic or header is referenced.",
          "evidenceIds": [
            "code-bee772150ea950a9a262"
          ]
        },
        {
          "code": "CODE-HIGH-ARM-CODE-SIMD-01",
          "magnitude": 11,
          "description": "high code finding \u0027ARM-CODE-SIMD-01\u0027: An x86 SIMD intrinsic or header is referenced.",
          "evidenceIds": [
            "code-c59d0a62b5152fad1b99"
          ]
        },
        {
          "code": "CODE-HIGH-ARM-CODE-SIMD-01",
          "magnitude": 11,
          "description": "high code finding \u0027ARM-CODE-SIMD-01\u0027: An x86 SIMD intrinsic or header is referenced.",
          "evidenceIds": [
            "code-d5ccc050fba167805167"
          ]
        },
        {
          "code": "CODE-HIGH-ARM-CODE-SIMD-01",
          "magnitude": 11,
          "description": "high code finding \u0027ARM-CODE-SIMD-01\u0027: An x86 SIMD intrinsic or header is referenced.",
          "evidenceIds": [
            "code-d8fcae520831fdbc3e4c"
          ]
        },
        {
          "code": "CODE-HIGH-ARM-CODE-SIMD-01",
          "magnitude": 11,
          "description": "high code finding \u0027ARM-CODE-SIMD-01\u0027: An x86 SIMD intrinsic or header is referenced.",
          "evidenceIds": [
            "code-efa2dab6e6e828ae1d99"
          ]
        },
        {
          "code": "CODE-HIGH-ARM-CODE-SIMD-01",
          "magnitude": 11,
          "description": "high code finding \u0027ARM-CODE-SIMD-01\u0027: An x86 SIMD intrinsic or header is referenced.",
          "evidenceIds": [
            "code-fa1862ef1cceaf8351c4"
          ]
        }
      ],
      "confidence": 0.95,
      "rationaleCodes": [
        "CODE-HIGH-ARM-CODE-SIMD-01",
        "CODE-MEDIUM-ARM-CODE-DYNAMIC-NATIVE-01"
      ],
      "evidenceIds": [
        "code-01f32f628a3a8c71316e",
        "code-07d36ddd828c6778134a",
        "code-0f09bccdb35f2acfa408",
        "code-173d76fa57352bb22a57",
        "code-1e783980f8cd8b8f2676",
        "code-29668645e6d38fa98606",
        "code-2abae4e6ad96adbdd5c9",
        "code-3d17ceb0a9867b2fa66a",
        "code-3d30fefc33da3df61bee",
        "code-4b92e91364291d65b865",
        "code-5611ea31124e5b950eac",
        "code-5709132da865245c8964",
        "code-5b442cb3164e6079ce5a",
        "code-7af5ae88a55536bf7078",
        "code-845886bcc044253617f0",
        "code-951349146b164a1d31d2",
        "code-aace821121d1a00e74dd",
        "code-b617305c170832eded41",
        "code-b6d360b3d8e80e77720a",
        "code-b6f1d139ff466798f171",
        "code-bdb2ad060dfd95a200b0",
        "code-bee772150ea950a9a262",
        "code-c59d0a62b5152fad1b99",
        "code-d5ccc050fba167805167",
        "code-d8fcae520831fdbc3e4c",
        "code-efa2dab6e6e828ae1d99",
        "code-fa1862ef1cceaf8351c4"
      ]
    },
    {
      "dimensionKey": "build-and-ci-readiness",
      "weightPct": 22,
      "rawScore": 15,
      "weightedContribution": 3.3,
      "deductions": [
        {
          "code": "BUILD-NO-ARM64",
          "magnitude": 50,
          "description": "Neither an ARM64 nor an Arm64EC build target was detected.",
          "evidenceIds": [
            "build-a411db87a55ff51b5b35"
          ]
        },
        {
          "code": "BUILD-NO-ARM64-CI",
          "magnitude": 20,
          "description": "No ARM64 CI job was detected.",
          "evidenceIds": [
            "build-a411db87a55ff51b5b35"
          ]
        },
        {
          "code": "BUILD-PKG-NO-ARM64",
          "magnitude": 15,
          "description": "Packaging does not declare ARM64 support.",
          "evidenceIds": [
            "build-a411db87a55ff51b5b35"
          ]
        }
      ],
      "confidence": 0.9,
      "rationaleCodes": [
        "BUILD-NO-ARM64",
        "BUILD-NO-ARM64-CI",
        "BUILD-PKG-NO-ARM64"
      ],
      "evidenceIds": [
        "build-a411db87a55ff51b5b35"
      ]
    },
    {
      "dimensionKey": "runtime-and-validation-evidence",
      "weightPct": 17,
      "rawScore": 60,
      "weightedContribution": 10.2,
      "deductions": [
        {
          "code": "RUN-DEP-RESOLUTION-LOW",
          "magnitude": 40,
          "description": "Dependency resolution rate 0 % below full.",
          "evidenceIds": []
        }
      ],
      "confidence": 0,
      "rationaleCodes": [
        "RUN-DEP-RESOLUTION-LOW"
      ],
      "evidenceIds": [
        "build-a411db87a55ff51b5b35"
      ]
    },
    {
      "dimensionKey": "windows-experience-and-deployment",
      "weightPct": 0,
      "rawScore": 22,
      "weightedContribution": 0,
      "deductions": [
        {
          "code": "WIN-UI-UNKNOWN-OR-ABSENT",
          "magnitude": 35,
          "description": "UI technology \u0027unknown\u0027 is unknown or absent.",
          "evidenceIds": []
        },
        {
          "code": "WIN-NO-INSTALLER",
          "magnitude": 15,
          "description": "No Windows installer detected.",
          "evidenceIds": []
        },
        {
          "code": "WIN-NO-OFFLINE",
          "magnitude": 8,
          "description": "App is not offline capable.",
          "evidenceIds": []
        },
        {
          "code": "WIN-A11Y-UNKNOWN",
          "magnitude": 10,
          "description": "Accessibility evidence not observed.",
          "evidenceIds": []
        },
        {
          "code": "WIN-NO-NOTIFICATIONS",
          "magnitude": 5,
          "description": "Windows notifications integration not detected.",
          "evidenceIds": []
        },
        {
          "code": "WIN-NO-LIFECYCLE",
          "magnitude": 5,
          "description": "Windows lifecycle integration not detected.",
          "evidenceIds": []
        }
      ],
      "confidence": 0.4,
      "rationaleCodes": [
        "WIN-A11Y-UNKNOWN",
        "WIN-DIM-SKIPPED-NOT-WINDOWS-APP",
        "WIN-NO-INSTALLER",
        "WIN-NO-LIFECYCLE",
        "WIN-NO-NOTIFICATIONS",
        "WIN-NO-OFFLINE",
        "WIN-UI-UNKNOWN-OR-ABSENT"
      ],
      "evidenceIds": []
    }
  ],
  "capsApplied": [
    {
      "capId": "no-arm64-or-arm64ec-target-le-60",
      "ceiling": 60,
      "description": "No ARM64 or Arm64EC build target detected.",
      "triggeredBy": [
        "build-a411db87a55ff51b5b35"
      ]
    }
  ],
  "majorBlockers": [
    {
      "blockerId": "bl-no-arm64-target",
      "category": "build",
      "description": "Build does not produce an ARM64 or Arm64EC target.",
      "evidenceIds": [
        "build-a411db87a55ff51b5b35"
      ]
    }
  ],
  "rationaleCodes": [
    "BUILD-NO-ARM64",
    "BUILD-NO-ARM64-CI",
    "BUILD-PKG-NO-ARM64",
    "CAP-NO-ARM64-OR-ARM64EC-TARGET-LE-60",
    "CODE-HIGH-ARM-CODE-SIMD-01",
    "CODE-MEDIUM-ARM-CODE-DYNAMIC-NATIVE-01",
    "DEP-UNKNOWN-REQUIRED",
    "RUN-DEP-RESOLUTION-LOW",
    "WIN-A11Y-UNKNOWN",
    "WIN-DIM-SKIPPED-NOT-WINDOWS-APP",
    "WIN-NO-INSTALLER",
    "WIN-NO-LIFECYCLE",
    "WIN-NO-NOTIFICATIONS",
    "WIN-NO-OFFLINE",
    "WIN-UI-UNKNOWN-OR-ABSENT"
  ],
  "rationaleDescriptions": {
    "BUILD-NO-ARM64": "Neither an ARM64 nor an Arm64EC build target was detected.",
    "BUILD-NO-ARM64-CI": "No ARM64 CI job was detected.",
    "BUILD-PKG-NO-ARM64": "Packaging does not declare ARM64 support.",
    "CAP-NO-ARM64-OR-ARM64EC-TARGET-LE-60": "No ARM64 or Arm64EC build target detected; overall score capped at 60.",
    "CODE-HIGH-ARM-CODE-SIMD-01": "A high-severity code finding was detected.",
    "CODE-MEDIUM-ARM-CODE-DYNAMIC-NATIVE-01": "A medium-severity code finding was detected.",
    "DEP-UNKNOWN-REQUIRED": "The ARM64 status of a required dependency could not be determined.",
    "RUN-DEP-RESOLUTION-LOW": "Dependency resolution rate is below full.",
    "WIN-A11Y-UNKNOWN": "Accessibility evidence could not be determined.",
    "WIN-DIM-SKIPPED-NOT-WINDOWS-APP": "Windows-experience dimension was skipped because no Windows-native surface was detected.",
    "WIN-NO-INSTALLER": "No Windows installer was detected.",
    "WIN-NO-LIFECYCLE": "Windows lifecycle integration was not detected.",
    "WIN-NO-NOTIFICATIONS": "Windows notifications integration was not detected.",
    "WIN-NO-OFFLINE": "The application is not offline-capable.",
    "WIN-UI-UNKNOWN-OR-ABSENT": "UI technology is unknown or absent."
  },
  "scoreSummary": "ComfyUI scored 14/100 (band blocked-or-major-redesign); evidence completeness medium (0.60). The \u0027no-arm64-or-arm64ec-target-le-60\u0027 cap fired at ceiling 60. The Windows-experience dimension was not scored because no Windows-native surface was detected. Largest deductions came from the dependency-compatibility dimension (raw 0/100) and the code-compatibility dimension (raw 0/100). Score is provisional (dependency-resolution-low, dimension-missing-evidence). Deterministic dispatch selects \u0027native-arm64\u0027 at confidence \u0027low\u0027.",
  "evidenceIds": [
    "build-a411db87a55ff51b5b35",
    "code-01f32f628a3a8c71316e",
    "code-07d36ddd828c6778134a",
    "code-0f09bccdb35f2acfa408",
    "code-173d76fa57352bb22a57",
    "code-1e783980f8cd8b8f2676",
    "code-29668645e6d38fa98606",
    "code-2abae4e6ad96adbdd5c9",
    "code-3d17ceb0a9867b2fa66a",
    "code-3d30fefc33da3df61bee",
    "code-4b92e91364291d65b865",
    "code-5611ea31124e5b950eac",
    "code-5709132da865245c8964",
    "code-5b442cb3164e6079ce5a",
    "code-7af5ae88a55536bf7078",
    "code-845886bcc044253617f0",
    "code-951349146b164a1d31d2",
    "code-aace821121d1a00e74dd",
    "code-b617305c170832eded41",
    "code-b6d360b3d8e80e77720a",
    "code-b6f1d139ff466798f171",
    "code-bdb2ad060dfd95a200b0",
    "code-bee772150ea950a9a262",
    "code-c59d0a62b5152fad1b99",
    "code-d5ccc050fba167805167",
    "code-d8fcae520831fdbc3e4c",
    "code-efa2dab6e6e828ae1d99",
    "code-fa1862ef1cceaf8351c4",
    "dependency-0209a8d233443e4ad955",
    "dependency-1fec3d082a9a15b05c0a",
    "dependency-270709d5ed238556cc99",
    "dependency-2f3617105f3296a059d6",
    "dependency-3442d4117305f1f58da4",
    "dependency-3ec1150f155b8fd5051a",
    "dependency-44cd82dbc52e46a6a6ad",
    "dependency-4c9ef52d52fba68c1646",
    "dependency-595a70b1a22872d2ae5a",
    "dependency-5cc12dab4268dcbe3700",
    "dependency-5efcd0aec421efcfafcd",
    "dependency-649e8fa0e536ce7f332f",
    "dependency-651f6c9fa1f5f9f6799c",
    "dependency-6b5cc84d468bee0568ce",
    "dependency-7875e31caf01e0572bb3",
    "dependency-7f9bd7f2a7df7f5a7391",
    "dependency-805f2565a46f25f6dd7a",
    "dependency-843725321ce800b7823d",
    "dependency-86bb1c22b8b9e6529c7a",
    "dependency-8b2625ff7ee1d1d30a53",
    "dependency-9f5278f94de0f2330fdb",
    "dependency-a33bde44e945be467683",
    "dependency-b1e184a7144f74db052a",
    "dependency-b35ad527ad90f7240528",
    "dependency-bb4afeb038eec462365c",
    "dependency-bf40c841f26a3c63e523",
    "dependency-c199703bd4a2c639ef88",
    "dependency-c2fbfd514f3ea1aed419",
    "dependency-cd069dc8547bbbb1c498",
    "dependency-d01eafc3fc88063cc987",
    "dependency-dafb551d9a27fb237c90",
    "dependency-e22fb8cd8caa1567977b",
    "dependency-e6c283166f5ddec2238a",
    "dependency-fa140c3b2b779fd231dc",
    "dependency-fc08762b6dd930acda43",
    "dependency-fc3843ea62fb3fc3910f",
    "dependency-fdfee7f25ce88bef9a44",
    "dependency-fea91d90d248f42f4cf6",
    "dependency-ff049b7c4a994e34bedb"
  ]
}
```

Score digest: `138fa8ba73d726ff4e5c6f0f6dcc8d42e30a8fff6e82a1072e93062a8a34c3dd`
