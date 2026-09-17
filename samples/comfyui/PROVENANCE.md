# ComfyUI — golden bundle provenance

Records exactly what produced the artifacts in this folder so the run can be
reconstructed inside the freeze window.

## Repo state used to produce this bundle

- **arm-migration-assist commit:** `dd3a561d689beb3721b63e3adee58da1a16c6a72` (main after PR #11)
- **Skill catalog version:** `2026-09-17.2` (from `knowledge/skills/catalog.json`)
- **Windows on Arm corpus version:** `2026-09-15.1`

## Feature 1 Assessment — local CLI

- **Producer:** `arm-migration-assist-repository-discovery` (see `assessment.json.producer`)
- **Invocation:** `dotnet run --project backend/Assessment/RepositoryDiscovery/RepositoryDiscovery.csproj --no-build -- https://github.com/comfyanonymous/ComfyUI --output samples/comfyui/assessment.json`
- **.NET SDK:** 9.0.318 (project targets `net8.0`)
- **Result:** schema-valid `RepositoryAssessmentV1` written to `assessment.json`
  (45 KB). Repository resolved to `master @ 387f98aa`.

## Feature 2 Planner — deployed API

- **Endpoint:** `https://ca-arm-migration-planner-api.delightfulcliff-b520a3d2.eastus2.azurecontainerapps.io`
- **Container App:** `ca-arm-migration-planner-api`
- **Resource group:** `rg-arm-migration-assist`
- **Deployed image tag at time of run:** `migration-planner-api:0.1.0`
- **Model provider (per response):** `foundry / gpt-4o / 2024.11.20`
- **Invocation:** `POST /api/migration-plans` with the on-disk `assessment.json`
- **Elapsed:** ~11 s including cold-path work
- **Result:** `runId = c892e36ea9fb4492925e29bdfc1e0c6c`, response split into
  `plan.json` and `score.json` (raw JSON preserved via `System.Text.Json`,
  not `ConvertTo-Json`).

## Score summary

- `overallScore`: **14 / 100**
- `band`: **`blocked-or-major-redesign`**
- `provisional`: **true**
- `recommendedPath` in plan: **`native-arm64`**

## Known gaps in this bundle

## Feature 4 Validation — local API against patched worktree

- **Producer:** `Validation.Api` at commit `b93c7d0` (main after PR #17).
- **Setup:**
  1. F3 patch `001-pipeline-github-actions-arm64-job.patch` (from `feature/f3-dry-run-comfyui`) applied to `C:\temp\comfyui-387f98a` and committed as `8878f4ee3d0c79d742fe71955dac96e55f6a7d59`. `core.autocrlf=false` locally.
  2. F4 API started on `http://127.0.0.1:5084` (loopback-only middleware) with `ValidationApi__StorageRoot=C:\temp\validation-api-storage`. No Foundry AI env vars set — deterministic-only workflow.
- **Invocation:** `POST /api/v1/validation/plans` with the plan's `validationPlan` + `workItems.acceptanceTests`, `target.path=C:\temp\comfyui-387f98a`, `target.commitSha=8878f4ee...`, `options.evidenceDirectory=C:\temp\comfyui-validation-evidence`.
- **Plan prepared:** `planId=67276a7e05d349fb84887d74264e37d1`, `status=prepared`, **0 commands** (deterministic planner honestly detected no `.csproj`/`.vcxproj`/`Dockerfile` supported for ComfyUI's pure-Python tree — refuses to fabricate commands), **9 criteria** (2 validation checks + 4 acceptance tests + 3 discovered/device placeholders).
- **Run:** empty approval → `POST /api/v1/validation/plans/{planId}/runs` → completed in ~2s.
- **Runner:** `windows/x64` (host machine, not ARM64 — noted explicitly in the report; native ARM64 evidence therefore unavailable).
- **Scorecard:** `status=not-validated`, 9 criteria all `NotRun`, **11 coverage gaps** including the two structural ones:
  - `missing-windows-arm64-runtime`: "No successful Windows ARM64 runtime evidence. Builds and Linux containers do not establish Windows on Arm readiness."
  - `missing-x64-comparison`: "No x64 baseline was requested or compared; no performance/regression parity claim can be made."

Bundle files added by F4:
- `validation-report.json` — full report + evidence + coverage gaps.
- `validation-dashboard.json` — versioned dashboard projection.

The `not-validated` outcome is the honest result for a pure-Python repo on a Windows x64 host: F3 emitted a workflow patch, F4 cannot compile-check Python nor run a Windows ARM64 smoke without ARM64 hardware. Rerunning F4 on Windows ARM64 hardware would exercise `native-arm64-runtime` criteria; the current gap list makes that requirement explicit.

## Known gaps in this bundle

- **`report.md` / `report.html` are absent.** The deployed image `0.1.0`
  predates the US 2.3 report-generation endpoint. `MemoryPlanArtifactStore` is
  in-memory only, so previous `runId`s are not retrievable across restarts. A
  redeploy of the container app is required before reports can be captured.
  Follow-up PR after `docker build/push` and `az containerapp update`.
- **`patches/` and `migration-result.json` are absent** until Automation
  Engineer runs Feature 3 against `plan.json` on a locally cloned ComfyUI at
  the pinned SHA.
- **`validation.json` is absent** until Feature 4 runs against the patched
  workspace.
