# ADR 0002: Deterministic scoring v2 — signal-scoped weights, cap suppression, human rationale

Status: Accepted
Date: 2026-09-16
Ruleset id: `scoring-v2` (supersedes `scoring-v1` from ADR 0001)
Applies to: `ReadinessScoreV1` produced by `DeterministicReadinessScorer` after this ADR lands.

## Context

Running `scoring-v1` against real assessments (ComfyUI in particular) surfaced
five gaps documented in the review pass:

1. The `no-arm64-or-arm64ec-target-le-60` cap fires for interpreted-language
   apps that never had a build target to hit. It ceilings the score at 60 for
   reasons the score itself already captures via the dependency dimension.
2. The Windows-experience dimension penalises non-Windows-native apps (web,
   CLI, unknown-with-no-Windows-surface) for missing signals they were never
   going to have.
3. The score exposes a `confidence` field that means "how complete the
   evidence is." The plan exposes a `confidence` field that means "how sure
   the recommendation is." They collide on the wire and produce contradictory
   reads for the caller.
4. Top-level `rationaleCodes[]` are opaque strings. Consumers who cannot
   resolve them (the model included) fall back to guessing.
5. Nothing in the score tells the caller — including the model — what the
   score *means* in one paragraph. The model is asked to produce
   `scoreInterpretation` from scratch every time.

`scoring-v2` addresses these five gaps plus a housekeeping fix on
`capsApplied[]`. Anything changed below MUST bump `Producer.ruleset` to a
future `scoring-v3`.

Ground rules from ADR 0001 still apply (determinism, unknowns never
positive, confidence separate from score, two-decimal weighted
contributions).

## Delta summary

| Area | v1 | v2 |
| --- | --- | --- |
| Ruleset id | `scoring-v1` | `scoring-v2` |
| Weights (dep/code/build/run/win) | 30/25/20/15/10 (fixed) | 30/25/20/15/10 OR 33/28/22/17/0 (signal-scoped) |
| `no-arm64-or-arm64ec-target-le-60` cap | Fires whenever build has no ARM64 target | Suppressed when the app is interpreted-only |
| Windows dimension | Always contributes 10% | Skipped (weight 0, contribution 0) for non-Windows apps |
| `capsApplied[]` | All fired caps | Only the effective (lowest-ceiling) cap |
| Score's own confidence field | `confidence` / `confidenceScore` | `evidenceCompleteness` / `evidenceCompletenessScore` (rename) |
| `rationaleDescriptions` | absent | present — map from code to one-line description |
| `scoreSummary` | absent | present — deterministic paragraph the model can lift |

## Interpreted-only detection

Motivation: interpreted stacks (Python, JavaScript, TypeScript, Ruby, PHP,
Perl, Lua) do not produce native binaries the way a `.csproj` or a Rust
`Cargo.toml` does. The `no-arm64-or-arm64ec-target-le-60` cap was
designed for compiled workloads where a missing target genuinely blocks
migration; it is a false blocker for interpreted workloads whose deps
already tell the ARM64 story.

Rule:

```
IsInterpretedOnly(assessment):
    interpreted = {"python", "javascript", "typescript", "ruby", "php",
                   "perl", "lua"}
    langs = assessment.Technology.Languages  (case-insensitive)
    return langs.Count > 0 AND langs.All(l => interpreted.Contains(l))
```

When `IsInterpretedOnly(assessment)` is true:

- The `no-arm64-or-arm64ec-target-le-60` cap is suppressed. It is NOT added
  to `capsApplied[]` even when its trigger condition is met.
- The build dimension keeps its raw deductions (missing target still costs
  50 points on the raw dimension score). It just does not ceiling the
  overall.
- A rationale code `BUILD-CAP-SUPPRESSED-INTERPRETED` is added to the build
  dimension so the suppression is auditable.

The other two caps (`required-unsupported-driver-le-30` and
`required-x64-only-native-le-40`) are NOT suppressed by interpreted-only;
a required driver or x64-only native binding still blocks migration in
that stack.

## Non-Windows-app detection (Windows dimension skip)

Motivation: apps with no Windows-native surface (browser web app, CLI-only,
"none") predictably score 0/100 on every Windows-experience signal. That
correctly identifies "not a Windows-native experience" but drags the
overall score down for a story the report cannot fix.

Rule:

```
IsNonWindowsApp(assessment):
    ui = assessment.WindowsExperience.UiTechnology
    if ui in {web, cli, none}: return true
    if ui == unknown:
        w = assessment.WindowsExperience
        hasAnyPositive =
             w.WindowsVersionExists
          || w.InstallerExists
          || w.OfflineCapable
          || w.NotificationsIntegrated
          || w.LifecycleIntegrated
          || w.AccessibilityEvidence in {full, partial}
        hasWindowsFramework =
             assessment.Technology.Frameworks intersects
             {winui3, wpf, winforms, uwp, maui}  (case-insensitive)
        return NOT hasAnyPositive AND NOT hasWindowsFramework
    return false
```

When `IsNonWindowsApp(assessment)` is true:

- The Windows dimension is still emitted (schema requires exactly five
  entries) but with `weightPct = 0` and `weightedContribution = 0`.
- Weights redistribute over the other four dimensions:

| Dimension key                      | Weight (Windows-scoped) | Weight (Skip-Windows) |
| ---------------------------------- | ----------------------: | --------------------: |
| dependency-compatibility           |                      30 |                    33 |
| code-compatibility                 |                      25 |                    28 |
| build-and-ci-readiness             |                      20 |                    22 |
| runtime-and-validation-evidence    |                      15 |                    17 |
| windows-experience-and-deployment  |                      10 |                     0 |
| **sum**                            |                     100 |                   100 |

- A rationale code `WIN-DIM-SKIPPED-NOT-WINDOWS-APP` is added to the Windows
  dimension. Its raw deductions are still computed and returned so
  consumers can see the underlying signals, but its contribution is 0.

## Rename: score's confidence → evidenceCompleteness

`ReadinessScoreV1` renames two fields. The values and banding rules
(`>=0.75` high, `>=0.50` medium, else low) are unchanged.

| v1 name           | v2 name                      |
| ----------------- | ---------------------------- |
| `confidence`      | `evidenceCompleteness`       |
| `confidenceScore` | `evidenceCompletenessScore`  |

The plan (`MigrationPlanV1`) `confidence` field is unchanged; it is the
recommendation-side confidence produced by `RecommendationDispatch`.
Callers now see two named-different values on the wire and can reason
about them independently.

## rationaleDescriptions

New top-level field on `ReadinessScoreV1`:

```
"rationaleDescriptions": {
  "DEP-EMULATION-REQUIRED": "One or more required dependencies run only under emulation on ARM64.",
  "BUILD-NO-ARM64":         "Neither an ARM64 nor an Arm64EC build target was detected.",
  ...
}
```

Contents: exactly the set of rationale codes emitted anywhere in the
score (top-level `rationaleCodes[]`, dimension-level `rationaleCodes[]`,
or embedded in `Deduction.code`). Descriptions are pulled from a central
lookup table in `ScoringRuleset`. Codes without an entry in the lookup
table get their own code string as the description (so the map is always
complete).

## scoreSummary

New top-level string on `ReadinessScoreV1`. Templated from the score
fields, so the same score always produces the same summary. Structure:

1. Overall score + band + evidence completeness ("scored X/100 (band …); evidence completeness Y").
2. Per-cap sentence ("The <cap description> cap fired at ceiling N.") for any fired cap, else
   "No blocker caps fired."
3. Suppression sentence when the interpreted-only or non-Windows-app rules
   fired.
4. Top-two dimension drivers ("The <dimension> dimension contributed the largest
   deduction (X points weighted).").
5. Recommended path sentence ("Deterministic dispatch selects <path> / <confidence>.").

The scoreSummary is emitted verbatim onto the score. The planner prompt
includes it under a dedicated section and instructs the model to lift or
paraphrase it into `scoreInterpretation`. The safety validator does not
enforce lift — the model is free to write its own interpretation as long
as the recommended path still matches the dispatch.

## capsApplied[] — one effective cap only

Motivation: `min-of-ceilings` is already the effective cap. Listing three
caps in `capsApplied[]` when only the strictest one ever bit implies the
score is triply-restricted, when in reality one restriction dominates and
the others are informational.

Rule: after computing all caps whose triggers fired, keep only the one
with the lowest ceiling in `capsApplied[]`. Break ties by the fixed cap
order defined in v1 (driver-first, then native, then no-target). The
would-be caps that did not make it into `capsApplied[]` still surface as
`majorBlockers[]` entries — that mechanism was already present in v1 and
is unchanged.

Consequence: `overallScore = min(uncappedScore, capsApplied[0].ceiling)`
when any cap fires, else `overallScore = uncappedScore`.

## ComfyUI worked example

Input snapshot: Python-only, 15 ready deps + 4 emulation-only required
deps (aiohttp, numpy, safetensors, tokenizers), no ARM64 build target,
`uiTechnology=unknown`, no positive Windows signals, coverage 1212/1212,
dep resolution 1.0, one Unknown with `area=windows-experience`.

Detection:

- `IsInterpretedOnly` → true (languages = [python]).
- `IsNonWindowsApp` → true (uiTech=unknown, all-false booleans, no
  Windows frameworks).

Weights: Skip-Windows (33/28/22/17/0).

Dimension raw scores (unchanged formulas):

| Dim | Raw | Weight | Weighted |
| --- | --: | -----: | -------: |
| dependency-compatibility (4 × -12) | 52 | 33 | 17.16 |
| code-compatibility                  | 100 | 28 | 28.00 |
| build-and-ci-readiness              | 15 | 22 | 3.30 |
| runtime-and-validation-evidence     | 100 | 17 | 17.00 |
| windows-experience-and-deployment   | 22 | 0 | 0.00 |
| **uncapped**                        |     |     | **65.46 → 65** |

Caps:

- `no-arm64-or-arm64ec-target-le-60` trigger fires — but suppressed by
  interpreted-only, so NOT added to `capsApplied[]`.
- Overall = 65 (uncapped, no ceiling).

Band: 65 in [50, 69] → `significant-remediation`.
evidenceCompleteness: ~0.79 → high.
provisional: true (windows-experience unknown emits
`dimension-missing-evidence`).

Dispatch (unchanged from v1):

- band != insufficient-evidence
- capsApplied is empty
- band is significant-remediation → path = `staged`

Hmm — under v1 the `no-target` cap fired, which mapped to `native-arm64`.
Under v2 the cap is suppressed and the band-only rule kicks in, which
gives `staged`. That change is intentional: an interpreted app with a
significant-remediation band and no cap really is a "staged" story
(migrate the four emulation-only deps first, then everything else falls
into place). If the caller wants "native-arm64" they need better dep
data, not a cap that lies.

## Producer stamp

```json
"producer": {
  "name": "arm-migration-assist-scorer",
  "version": "0.2.0",
  "ruleset": "scoring-v2"
}
```

Every subsequent change to weights, deductions, cap ceilings, banding, or
detection heuristics MUST bump `ruleset` to a future `scoring-v3`.
