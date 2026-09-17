# AI Migration Planner Implementation Plan

## Purpose

Build the first locally runnable .NET 8 implementation of ARM Migration Assist Feature 2. The planner accepts a versioned, evidence-based repository assessment, calculates a deterministic Windows on Arm readiness score, uses an AI reasoning agent to create a migration plan, validates the result, and returns an auditable planning artifact.

This iteration is planning-only. It must not modify repositories, execute builds or arbitrary commands, publish artifacts, create commits, or open pull requests.

## Challenge Alignment

The implementation supports the hackathon objective of making Windows applications faster, more reliable, power-efficient, offline-ready, and more natural on Windows on Arm devices. It must support Native ARM64, Arm64EC, staged migration, WinUI 3, and insufficient-evidence outcomes.

The human operator supplies prompts, reviews outputs, and approves future write-capable actions. Application changes and migration recommendations are produced through the agent and reusable skills; the planner never assigns a human to write code manually.

## Architecture

Create a .NET 8 solution with these boundaries:

- ASP.NET Core minimal API for the HTTP surface and Problem Details responses.
- Application layer for planner orchestration and workflow state.
- Domain/contracts layer for versioned assessment, score, plan, evidence, and skill models.
- Infrastructure layer for model providers, MCP adapters, audit logging, and configuration.
- Unit tests for contracts, scoring, validation, and safety rules.
- Integration tests for the API, mock MCP server, fake model, and all fixtures.
- JSON Schema, fixture, decision-record, and prompt-log directories.

Depend on interfaces rather than concrete model or MCP implementations so the mock components can later be replaced without changing planner contracts.

## Implementation Phases

### 1. Contracts and schemas

Define `RepositoryAssessmentV1`, `ReadinessScoreV1`, and `MigrationPlanV1`.

Enforce required properties, strict enums, schema versions, confidence ranges, no additional output properties, and unique evidence IDs. Check JSON Schema at the API boundary and again before returning a plan.

### 2. Evidence and skill validation

Implement validators for:

- Duplicate or malformed evidence IDs.
- Evidence missing source type, path or artifact name, or observation.
- References to evidence that does not exist.
- Unknown agent or skill names.
- Valid proposed missing skills.
- Invalid work-item dependencies and cyclic sequences.
- Missing approval requirements.
- Manual coding, repository mutation, unrestricted execution, publishing, commit, or pull-request instructions.

### 3. Deterministic readiness scoring

Implement the five initial weighted dimensions. Each dimension below states what it measures, the signals the scorer reads, why it carries the weight it does, and how it links to caps or the provisional flag. Illustrative formulas and deduction magnitudes live in the scoring decision record; the schema field names come from `RepositoryAssessmentV1`.

**Dependency compatibility — 30%.** How ready the app's dependencies are for ARM64. Signals: each dependency's ARM64 availability status (`ready` / `emulation-only` / `unknown` / `blocked`), whether it is `required` vs `optional`, and whether replacement candidates exist. It carries the heaviest weight because a single blocked required native/x64-only dependency can make migration infeasible. Drives the `≤ 30` unsupported-driver cap and the `≤ 40` required x64-only-native cap.

**Code compatibility — 25%.** Whether the source code contains architecture-specific patterns that break on ARM64 — inline x86/x64 assembly, SSE/AVX intrinsics, hard-coded `x64` conditionals, P/Invoke into x64-only libraries. Scored by counting `codeFindings[]` weighted by severity (critical 25, high 12, medium 5, low 1, informational 0) and by finding confidence. Concentrated critical findings can push the score below the `blocked` band even without a cap.

**Build and CI readiness — 20%.** Whether the repo already produces ARM64 artifacts. Signals: `arm64TargetExists`, `arm64EcTargetExists`, `arm64CiJobExists`, `packagingSupportsArm64`, `testsExist`. If neither an ARM64 nor an Arm64EC target exists, the final score is capped at 60.

**Runtime and validation evidence — 15%.** Verifiable behavioral evidence that the migration can be validated, not guessed at. Signals: `testsExist`, `scanCoverage.dependencyResolutionRate`, ratio of `scannersCompleted` vs `scannersFailed`, and any prior ARM runtime signals. Low coverage here flips the overall result to `provisional`.

**Windows experience and deployment readiness — 10%.** How Windows-native the app already is, which predicts deployment friction on Windows on Arm. Signals: `uiTechnology` (WinUI 3 / WPF / WinForms score highest; Electron / Qt mid; web / CLI low), `installerExists`, `offlineCapable`, `accessibilityEvidence`, `notificationsIntegrated`, `lifecycleIntegrated`. Weakness here nudges the planner toward a WinUI 3 modernization alternative.

The five weighted raw scores are summed to an uncapped score, then blocker caps and a confidence calculation are applied to produce the final banded result (`ready-or-minor-changes` 85–100, `moderate-migration` 70–84, `significant-remediation` 50–69, `blocked-or-major-redesign` 0–49).

The scorer must return raw scores, deductions, weighted contributions, supporting evidence IDs, uncapped score, final score, confidence, provisional state, rationale codes, major blockers, and applied caps.

Use configurable scoring rules. Apply deductions and blocker caps deterministically, calculate confidence separately, and never treat unknown information as a positive result.

Initial caps:

- Required unsupported driver: maximum 30.
- Required x64-only native dependency without replacement: maximum 40.
- No ARM64 or Arm64EC build target: maximum 60.

Initial bands:

- 85-100: ready-or-minor-changes.
- 70-84: moderate-migration.
- 50-69: significant-remediation.
- 0-49: blocked-or-major-redesign.

### 4. Read-only MCP abstraction

Define typed interfaces and an explicit allowlist for:

- `get_repository_summary`
- `get_dependency_findings`
- `get_code_compatibility_findings`
- `get_build_readiness_findings`
- `get_windows_experience_findings`
- `get_scan_coverage`
- `get_available_skill_catalog`
- `calculate_readiness`
- `lookup_windows_arm_guidance`

Implement a fixture-backed mock server. Every result must contain evidence IDs. Reject unknown tools and prohibit write, build, shell, commit, publish, and pull-request operations.

#### How the model invokes tools

The planner uses a hybrid invocation pattern so the same contract works for the fake provider, hosted providers with tool-calling, and local providers without it.

- **Orchestrator-prefetched tools.** The orchestrator invokes the following read-only tools before any model call and packs their JSON results into structured prompt sections: `get_repository_summary`, `get_dependency_findings`, `get_code_compatibility_findings`, `get_build_readiness_findings`, `get_windows_experience_findings`, `get_scan_coverage`, `get_available_skill_catalog`, `calculate_readiness`. The model never chooses whether to call these.
- **Model-invoked tool.** Only `lookup_windows_arm_guidance` is exposed to the model as a callable tool, because it is the one call where the model benefits from choosing what to retrieve. Providers that support function/tool calling expose it through a narrow `IGuidanceLookup` view over the registry; the orchestrator runs a bounded loop (maximum 8 calls, maximum 30 s wall time, deduplicated `guidanceId` set).
- **Non-tool-calling fallback.** Providers without tool calling (the fake provider, an optional local Phi) additionally receive a compact `guidance_index` (list of `guidanceId`, `title`, `section`) in the prompt and must cite `guidanceId` values in `guidanceCitations[]`. The plan validator resolves and rejects unknown IDs.
- **Allowlist enforcement.** The tool loop always resolves through `IMcpToolRegistry`. Any call to a name outside the model-invocable allowlist (currently only `lookup_windows_arm_guidance`) is rejected as if unknown. Registry rejections and loop-bound exhaustion produce `Planner.ModelToolViolation` and `Planner.ModelToolBudgetExceeded` error codes respectively.
- **Audit.** Every prefetched and model-invoked tool call is recorded in the audit event's `mcpToolsInvoked` list with tool name, evidence IDs returned, and, for guidance calls, the resolved `guidanceId` values. Redacted arguments only; no raw source content.

#### Grounded Windows on Arm guidance corpus

`lookup_windows_arm_guidance` must serve a curated, versioned snapshot of Microsoft Windows on Arm documentation, not perform live web access. This preserves the "use only supplied evidence and approved Windows on Arm guidance" rule from the planner's system instructions, keeps recommendations reproducible, and closes a prompt-injection surface.

Include curated snapshots of at least:

- Windows on Arm overview and getting-started guidance.
- Add Arm support to your Windows app.
- Arm64EC overview and ABI reference.
- Update app architecture from Arm32 to Arm64.
- WinUI 3 and Windows App SDK references relevant to porting.
- MSIX packaging guidance for ARM64.

Each snippet must include:

- Stable `guidanceId`.
- `sourceUrl`, `title`, and `section`.
- `retrievedAt` timestamp.
- Excerpt suitable for citation.
- `corpusVersion` shared across the snapshot.

Rules for guidance handling:

- Treat each snippet as evidence. Planner facts, inferences, alternatives, work items, risks, and validation checks may cite `guidanceId` values alongside assessment `evidenceId` values.
- The plan validation pipeline must resolve every `guidanceId` referenced by the model. Unknown IDs are treated as hallucinations and fail validation.
- The tool must reject requests for arbitrary URLs or free-text web queries. Only bundled corpus snippets are returnable in this iteration.
- Refreshing the corpus is a maintainer-only, offline job. The planner never triggers a refresh.
- Audit logs must record `corpusVersion` and which `guidanceId` values were retrieved during a run.

The corpus lives under `knowledge/windows-on-arm/` with one file per snippet plus an index manifest. Later iterations may replace the local corpus with an approved hosted knowledge index without changing planner contracts.

### 5. Planner orchestration

The orchestration flow is:

1. Validate the assessment and schema version.
2. Load the available skill catalog.
3. Prefetch the read-only MCP tools listed in section 4 and cache their responses for the run.
4. Calculate the deterministic readiness score.
5. Build structured planner input containing: prefetched tool responses, deterministic score, skill catalog, guidance index, and system instructions.
6. Call `IPlannerModel` with an `IGuidanceLookup` handle; drive the bounded guidance tool-calling loop (or accept `guidanceId` citations from non-tool-calling providers) until the provider returns a final `MigrationPlanV1` JSON.
7. Parse and validate the model JSON against `MigrationPlanV1`.
8. Verify assessment ID, evidence, skills, dependencies, approvals, and safety rules.
9. Verify that the model did not alter the deterministic score.
10. Resolve every `guidanceId` in `guidanceCitations[]`; unknown IDs fail validation.
11. Return the validated plan and warnings.
12. Record the audit event with prefetched tools, model-invoked guidance IDs, provider metadata, latency, and token usage where available.

The model must separate facts, inferences, recommendations, and unknowns. Every material claim must be evidence-linked where applicable.

### 6. Model hosting and providers

The planner depends on `IPlannerModel`, not on any concrete host, so the hosting decision can change without touching planner contracts. Provider selection is driven by the `MIGRATIONPLANNER_MODEL_PROVIDER` environment variable, resolved into the `PlannerModelProvider` enum at DI registration time.

#### Provider tiers

- **`Fake` (default).** `FakePlannerModel` returns deterministic canned `MigrationPlanV1`-shaped JSON keyed by `assessmentId`. No credentials, no network, no tool-calling. Used for local runs, fixture tests, and CI without cloud access.
- **`Hosted` — Azure OpenAI or approved internal endpoint.** Reserved for a future provider that supports structured output and function/tool calling. Not implemented in this iteration; selecting it throws at startup.
- **`Phi` — Azure AI Foundry.** `PhiPlannerModel` targets a Phi deployment on an Azure AI Foundry (`kind=AIServices`) account via the Azure AI Model Inference API (`Azure.AI.Inference`). Phi does not reliably support OpenAI-style function calling, so this provider uses the non-tool-calling code path from section 4: the orchestrator embeds the guidance index in the prompt, the model cites `guidanceId` values in `guidanceCitations[]`, and the plan validator resolves them post-hoc. Endpoint, deployment name, timeout, and token limits come from configuration.

#### Cross-provider requirements

Every non-fake provider must:

- Use standard application configuration for endpoint, model name/deployment, and credentials. Prefer managed identity over static keys.
- Emit structured output where supported; validate the response against `MigrationPlanV1` regardless.
- Apply bounded retries, per-call timeouts, and deterministic sampling settings (temperature 0 or lowest supported).
- Route any `lookup_windows_arm_guidance` invocation through the injected `IGuidanceLookup`; never call MCP tools directly or reach the network for guidance content.
- Record provider name, model/deployment, latency, token usage when available, retry count, and success/failure in the audit event.
- Never log raw repository source content, assessment payloads, or full prompts by default. Redaction rules live with the audit logger.

#### Extension point

Adding a new hosted provider is a matter of implementing `IPlannerModel`, registering it in the DI extension, and adding its enum value. The orchestration flow, tool allowlist, guidance loop, and validation pipeline stay unchanged.

### 7. Minimal API

Implement `POST /api/migration-plans` with the specified request and response contracts.

Return RFC 9457 Problem Details for malformed input, unsupported schemas, invalid evidence, unavailable tools, model failures, invalid model output, and safety validation failures. Use stable error codes and avoid exposing secrets or source content.

### 8. Auditability

Record run ID, timestamps, assessment ID, commit SHA, schema version, provider/model, MCP tools invoked, redacted tool arguments, evidence IDs accessed, deterministic score, plan-validation result, errors, and retry count.

Use structured events so local logging can later be replaced by durable storage.

### 9. Fixtures and tests

Create four complete fixtures:

- `ready-managed-app`: expected `native-arm64`.
- `moderate-build-gap`: expected `native-arm64` with build-focused work.
- `blocked-native-app`: expected `arm64ec` or `staged`, with a blocker cap.
- `incomplete-assessment`: expected `insufficient-evidence`, provisional.

Test schema validation, unsupported versions, duplicate evidence, score stability, each blocker cap, confidence, provisional results, MCP allowlisting, tool-call loops, valid and invalid model responses, hallucinated evidence, unknown skills, missing-skill declarations, dependency cycles, unsafe instructions, timeout/retry behavior, and end-to-end fixture outcomes.

### 10. Documentation and extension points

Document architecture, contracts, scoring rules, local setup, fixture processing, API examples, mock replacement, Feature 1 integration, security boundaries, and the deterministic-scoring decision record.

Include a prompt log documenting that implementation was generated through the agentic workflow.

### 11. Migration report generation (User Story 2.3)

Turn the validated `MigrationPlanV1` + `ReadinessScoreV1` pair into a portable, human-readable report an engineering lead can drop into a PR description, a Teams post, or an email. Reports are pure renderings of an already-produced plan artifact: rendering **must not** call the model, mutate the plan, add new claims, or fabricate URLs. When a field is empty in the plan, it stays empty in the report.

**Supported formats.**

- MVP: **Markdown** (primary) and **HTML** (single self-contained file, printable → "Save as PDF").
- Fast follow (out of scope for MVP): **PDF** via QuestPDF and a `.zip` audit bundle (`report.md`, `plan.json`, `score.json`, `assessment.json`).

**Contracts (Application layer).**

- `MigrationReport` — view model wrapping `RunId`, `MigrationPlanV1`, `ReadinessScoreV1`, warnings, and a lifted `RepositoryIdentity` (name, url, commitSha, defaultBranch).
- `ReportFormat` — enum (`Markdown`, `Html`, later `Pdf`).
- `IMigrationReportRenderer` — one implementation per format, keyed by `Format`, exposing `ContentType`, `FileExtension`, and `Task<Stream> RenderAsync(MigrationReport, CancellationToken)`.
- `MigrationReportFactory` — deterministic projection from `(RepositoryAssessmentV1, PlanResult)` to `MigrationReport`. Rejects failed `PlanResult`s.
- `IPlanArtifactStore` — persists successful runs so reports remain retrievable after the fact. In-memory implementation for MVP; interface allows swapping to durable storage without touching renderers.

**Renderers (Infrastructure layer).**

- `MarkdownReportRenderer` — pure `StringBuilder`, no dependencies. Deterministic byte output for golden-file snapshot tests.
- `HtmlReportRenderer` — single embedded HTML template with `{{token}}` substitution. No JavaScript, inline CSS only, safe for offline viewing.

**Report layout.** Sections are ordered evidence → verdict → action, not by JSON field order:

1. Header: repo name, commit SHA, generatedAt, model provenance, corpus version, `runId`, `scoreDigest`.
2. Verdict: `recommendedPath`, `confidence`, banded overall score, applied caps, provisional flag.
3. Executive summary (verbatim from `plan.executiveSummary`).
4. Score interpretation + a table of the five weighted dimensions (raw, weighted, deductions).
5. Facts (`plan.facts[]`) with evidence and `guidanceId` citations rendered as footnote-style anchors.
6. Inferences (`plan.inferences[]`), labeled "planner-inferred".
7. Recommended work items grouped by priority, with `agentOrSkill`, dependencies, required approvals.
8. Alternatives considered (`plan.alternatives[]`) with disposition and rationale.
9. Risks + unknowns (combined table).
10. Validation plan (`plan.validationPlan[]`).
11. Missing skills + required approvals (gating items).
12. Appendix: full plan JSON and score JSON.

**HTTP surface.**

- `GET /api/migration-plans/{runId}/report.{ext}` — `ext ∈ { md, html }`.
- 404 + Problem Details for unknown `runId`.
- 415 + Problem Details for unsupported `ext`.
- `Content-Disposition: attachment; filename=migration-report-{runId}.{ext}` for browser downloads.

**Guardrails.**

- Rendering is stateless and side-effect-free apart from reading the artifact store.
- `guidanceId` citations resolve against the same corpus version recorded on the plan; unknown IDs are dropped, not fabricated. `sourceUrl` values are copied verbatim from the corpus snapshot.
- `scoreDigest` is rendered in the footer of every format so a reader can prove the report was produced from an untampered score.
- Renderers never emit secrets, raw source content, or full prompts (matches the audit-logger rule).

**Phased delivery.**

1. Contracts, `MigrationReport` view model, `MigrationReportFactory`, unit tests (this step).
2. `MarkdownReportRenderer` + golden-file snapshot tests against the three demo fixtures.
3. `IPlanArtifactStore` + in-memory implementation, wired into the existing planning service so runs are persisted on success.
4. `HtmlReportRenderer` + golden-file snapshot tests.
5. Report download endpoint + integration tests.
6. Demo UI download buttons ("Download report (Markdown)", "Download report (HTML)").
7. Populate `ReportGeneration/README.md` with the contract and format matrix.

## Definition of Done

- The .NET 8 solution builds successfully.
- All tests pass without cloud credentials.
- Every fixture produces a validated plan.
- Blocker caps and provisional scoring behave as specified.
- Recommendations, findings, work items, and risks are evidence-linked.
- Every work item maps to an available or proposed reusable skill.
- All work items require approval.
- No component modifies a repository or executes arbitrary commands.
- The hosted provider can be added without changing planner contracts.
- Documentation explains how to connect the future Feature 1 MCP server and approved model endpoint.
