# ADR 0001: Deterministic Windows-on-Arm readiness score (v1)

Status: Accepted
Date: 2026-09-16
Ruleset id: `scoring-v1`
Applies to: `ReadinessScoreV1` (schema `1.0`) produced by
`DeterministicReadinessScorer` in `MigrationPlanner.Infrastructure.Scoring`.

## Context

`IMPLEMENTATION_PLAN.md` §3 lists the five weighted dimensions, three blocker
caps, four bands, and rules for confidence and the provisional flag, but
delegates the concrete formulas and deduction magnitudes to a decision
record. This ADR is that record. Anything changed below MUST bump the
`Producer.ruleset` value ("`scoring-v1`" → "`scoring-v2`") so downstream audit
records and score digests remain reproducible.

Ground rules the scorer honours everywhere:

- Deterministic and pure. Same `RepositoryAssessmentV1` in ⇒ byte-identical
  `ReadinessScoreV1` out. No wall clock, no network, no RNG. `generatedAt` is
  passed in by the orchestrator, not read from `DateTimeOffset.UtcNow` inside
  the scorer.
- Unknown information is never a positive signal. It either produces a small
  fixed deduction or lowers confidence, but never raises a raw score.
- Confidence is computed independently of the score. A weak assessment cannot
  raise the score through high confidence, and it cannot lower the score
  through low confidence.
- Deductions are integer points. Weighted contributions and confidence keep
  two decimal places (schema `$comment`s recommend that for canonicalization
  stability).

## Weights

Fixed per schema and plan.

| Dimension key                        | Weight |
| ------------------------------------ | -----: |
| `dependency-compatibility`           |    30% |
| `code-compatibility`                 |    25% |
| `build-and-ci-readiness`             |    20% |
| `runtime-and-validation-evidence`    |    15% |
| `windows-experience-and-deployment`  |    10% |
| **Total**                            |  100% |

Weights sum to 100 (schema cross-record rule).

## Dimension 1 — Dependency compatibility (30%)

Signal: each `DependencyFinding` (`type`, `criticality`, `architectureStatus`,
`replacementCandidates`).

Raw score starts at 100. Per finding, subtract the following magnitude and
emit the listed rationale code, evidence-linked to the finding's
`evidenceId`.

| Criticality | Arch status     | Replacement candidates | Deduction | Rationale code                      |
| ----------- | --------------- | ---------------------- | --------: | ----------------------------------- |
| required    | blocked         | none                   |        30 | `DEP-BLOCKED-REQUIRED-NO-REPL`      |
| required    | blocked         | ≥1                     |        20 | `DEP-BLOCKED-REQUIRED-WITH-REPL`    |
| required    | emulation-only  | any                    |        12 | `DEP-EMULATION-REQUIRED`            |
| required    | unknown         | any                    |         8 | `DEP-UNKNOWN-REQUIRED`              |
| optional    | blocked         | none                   |         6 | `DEP-BLOCKED-OPTIONAL`              |
| optional    | blocked         | ≥1                     |         4 | `DEP-BLOCKED-OPTIONAL-WITH-REPL`    |
| optional    | emulation-only  | any                    |         3 | `DEP-EMULATION-OPTIONAL`            |
| optional    | unknown         | any                    |         2 | `DEP-UNKNOWN-OPTIONAL`              |
| any         | ready           | any                    |         0 | —                                   |

Final raw score = `clamp(100 − Σ deductions, 0, 100)`.

Cap triggers (see §Caps):

- Any finding with `criticality=required`, `type=driver`, `architectureStatus=blocked`
  → fires `required-unsupported-driver-le-30` (ceiling 30).
- Any finding with `criticality=required`, `type=native` (or `com`),
  `architectureStatus=blocked`, and `replacementCandidates` empty
  → fires `required-x64-only-native-le-40` (ceiling 40).

Dimension confidence: mean of the touched findings' `confidence` values,
defaulting to `0.60` when the dependency list is empty. Subtract `0.15` if
any `Unknown.area=dependency` entry is present. Clamp to `[0, 1]`.

## Dimension 2 — Code compatibility (25%)

Signal: `codeFindings[]` (`severity`, `confidence`).

Raw score starts at 100. Per finding, deduction = `round(severityWeight ×
confidence)`.

| Severity        | Severity weight |
| --------------- | --------------: |
| critical        |              25 |
| high            |              12 |
| medium          |               5 |
| low             |               1 |
| informational   |               0 |

Rationale code per deduction: `CODE-<SEVERITY>-<RULEID-UPPERCASED>`. If the
`ruleId` produces characters outside `[A-Za-z0-9-]`, they are replaced with
`-` and collapsed. Codes are deduplicated per dimension.

Final raw score = `clamp(100 − Σ deductions, 0, 100)`.

The scorer does not fire a blocker cap from code findings; concentrated
critical findings simply push the raw score (and hence the weighted
contribution) into the blocked band on their own.

Dimension confidence: mean of touched findings' `confidence`, defaulting to
`0.70` when there are no code findings **and** at least one code scanner is
listed in `scanCoverage.scannersCompleted`; otherwise `0.40`. Subtract
`0.15` if any `Unknown.area=code` entry is present. Clamp to `[0, 1]`.

## Dimension 3 — Build and CI readiness (20%)

Signal: `buildFindings` booleans.

Raw score starts at 100. Deduct:

| Condition                                                   | Deduction | Rationale code           |
| ----------------------------------------------------------- | --------: | ------------------------ |
| `!arm64TargetExists && !arm64EcTargetExists`                |        50 | `BUILD-NO-ARM64`         |
| `arm64TargetExists && !arm64EcTargetExists` (or vice versa) |         0 | —                        |
| `!arm64CiJobExists`                                         |        20 | `BUILD-NO-ARM64-CI`      |
| `!packagingSupportsArm64`                                   |        15 | `BUILD-PKG-NO-ARM64`     |
| `!testsExist`                                               |        15 | `BUILD-NO-TESTS`         |

Final raw score = `clamp(100 − Σ deductions, 0, 100)`.

Cap: `!arm64TargetExists && !arm64EcTargetExists` fires
`no-arm64-or-arm64ec-target-le-60` (ceiling 60).

Dimension confidence: `0.90` if the build findings block is present (it
always is, since `BuildFindings` is required by the assessment schema). Drop
to `0.60` if any `Unknown.area=build` entry is present.

## Dimension 4 — Runtime and validation evidence (15%)

Signal: `buildFindings.testsExist`, `scanCoverage`.

Raw score starts at 100.

- `!testsExist` → deduct 30, code `RUN-NO-TESTS`.
- Let `coverageRate = min(1, filesScanned / max(1, filesTotal))`. Deduct
  `round((1 − coverageRate) × 40)`, code `RUN-COVERAGE-LOW`, only when
  the deduction is > 0.
- Let `resolutionRate = clamp(dependencyResolutionRate, 0, 1)`. Deduct
  `round((1 − resolutionRate) × 40)`, code `RUN-DEP-RESOLUTION-LOW`, only
  when the deduction is > 0.
- Deduct `min(10 × scannersFailed.Count, 40)`, code `RUN-SCANNER-FAILED`,
  only when the deduction is > 0.

Final raw score = `clamp(100 − Σ deductions, 0, 100)`.

Dimension confidence: `min(coverageRate, resolutionRate)`, defaulting to `0`
if the denominator is zero. Multiply by `0.5` if `scannersFailed.Count > 0`.

## Dimension 5 — Windows experience and deployment (10%)

Signal: `windowsExperience`.

Raw score starts at 100. Deduct:

| Condition                                | Deduction | Rationale code              |
| ---------------------------------------- | --------: | --------------------------- |
| UI = WinUI3                              |         0 | —                           |
| UI ∈ {WPF, WinForms, UWP, MAUI, XamlIslands} | 10   | `WIN-UI-LEGACY`             |
| UI ∈ {Electron, Qt, Tauri, Flutter, Gtk} |        25 | `WIN-UI-CROSS-PLATFORM`     |
| UI ∈ {Web, CLI, None, Other, Unknown}    |        35 | `WIN-UI-UNKNOWN-OR-ABSENT`  |
| `!installerExists`                       |        15 | `WIN-NO-INSTALLER`          |
| `!offlineCapable`                        |         8 | `WIN-NO-OFFLINE`            |
| accessibility = none                     |        15 | `WIN-A11Y-NONE`             |
| accessibility = partial                  |         8 | `WIN-A11Y-PARTIAL`          |
| accessibility = unknown                  |        10 | `WIN-A11Y-UNKNOWN`          |
| `!notificationsIntegrated`               |         5 | `WIN-NO-NOTIFICATIONS`      |
| `!lifecycleIntegrated`                   |         5 | `WIN-NO-LIFECYCLE`          |

Final raw score = `clamp(100 − Σ deductions, 0, 100)`.

Dimension confidence: `0.80` if `uiTechnology != Unknown`, else `0.40`. Drop
to `0.60` if any `Unknown.area=windows-experience` entry is present.

## Weighted uncapped score

```
weightedContribution_d = rawScore_d × weightPct_d / 100      // per dimension
uncappedScore          = round(Σ weightedContribution_d)     // integer 0..100
```

## Caps

Applied in a fixed order, lowest ceiling first, so audit output is stable
regardless of which caps fire together:

1. `required-unsupported-driver-le-30`   — ceiling `30`
2. `required-x64-only-native-le-40`      — ceiling `40`
3. `no-arm64-or-arm64ec-target-le-60`    — ceiling `60`

```
overallScore = min(uncappedScore, min(ceilings of all fired caps))
```

Every cap emits a `CapApplication` with `triggeredBy` = the evidence IDs of
the findings that caused it (deduplicated, up to the schema cap of 50).

## Major blockers

Emitted alongside caps and (independently) for high-severity code findings
so the planner can reason over them as first-class items.

- `bl-drv-<name-slug>` (category `dependency`) — one per required driver in
  `blocked` state.
- `bl-native-<name-slug>` (category `dependency`) — one per required native/COM
  dependency in `blocked` state with no replacement candidates.
- `bl-no-arm64-target` (category `build`) — emitted when the no-target cap
  fires; `triggeredBy` = `buildFindings.evidenceId`.
- `bl-code-crit-<ruleId-slug>` (category `code`) — one per code finding with
  `severity=critical`.

Slugification: lowercase, non-alphanumeric characters collapsed to `-`,
trimmed to fit the schema pattern `^bl-[a-z0-9-]{2,80}$`.

## Confidence

Top-level `confidenceScore` = weighted mean of dimension confidences (same
weights as the score). Clamp to `[0, 1]` and round to 2 decimal places.

Banded `confidence` label:

- `>= 0.75` → `high`
- `>= 0.50` → `medium`
- else       → `low`

## Provisional flag and reasons

`provisional = true` when any of the following fires. Reasons are emitted in
this deterministic order and deduplicated.

| Trigger                                                                 | ProvisionalReason              |
| ----------------------------------------------------------------------- | ------------------------------ |
| `filesTotal > 0` and `filesScanned / filesTotal < 0.60`                 | `scan-coverage-low`            |
| `dependencyResolutionRate < 0.60`                                       | `dependency-resolution-low`    |
| `scannersFailed.Count > 0`                                              | `scanner-failed`               |
| Any `Unknown.area` ∈ {`dependency`,`code`,`build`,`windows-experience`} | `dimension-missing-evidence`   |
| `filesTotal == 0`                                                       | `insufficient-signals`         |

## Banding

```
insufficient-evidence  = provisional && (filesTotal == 0 || coverageRate < 0.30)
ready-or-minor-changes = overallScore in [85, 100]
moderate-migration     = overallScore in [70, 84]
significant-remediation= overallScore in [50, 69]
blocked-or-major-redesign = overallScore in [0, 49]
```

The `insufficient-evidence` band overrides the numeric band when its
condition fires.

## Rationale-code taxonomy

Prefix map (matches schema pattern `^[A-Z]{2,10}(-[A-Za-z0-9]+){1,10}$`):

| Prefix  | Meaning                     |
| ------- | --------------------------- |
| `DEP-`  | Dependency-compatibility    |
| `CODE-` | Code-compatibility          |
| `BUILD-`| Build and CI readiness      |
| `RUN-`  | Runtime and validation      |
| `WIN-`  | Windows experience          |
| `CAP-`  | Emitted by a fired cap      |

Top-level `rationaleCodes` is the deduplicated union of all dimension-level
codes plus one `CAP-<capId>` entry per fired cap.

## Producer stamp

Every score document embeds:

```json
"producer": {
  "name": "arm-migration-assist-scorer",
  "version": "0.1.0",
  "ruleset": "scoring-v1"
}
```

`version` tracks the assembly version. `ruleset` tracks this ADR. Both are
recorded in every audit event so a score can be traced back to the exact
rules that produced it.
