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

## Feature 3 AutomatedMigration — CLI, cherry-picked from an earlier dry-run

- **Producer:** `AutomatedMigration.MigrationActionsRunner` at commit `3765849` (post-reconciliation main).
- **Invocation:** `dotnet run --project backend/AutomatedMigration/AutomatedMigration.csproj --no-build -- <plan.json> C:\temp\comfyui-387f98a C:\temp\comfyui-f3-output` (dry-run, no `--publish`).
- **Source of these files:** cherry-picked from branch `feature/f3-dry-run-comfyui`. That run posted an earlier assessment to the deployed F2 (Fake provider) and got planId `plan-baeca60e2e194`, runId `baeca60e2e19484abe72925257ce3968`. Both derive from the same `assessment-b5a29c9cc32e432cc9338f9b` — deterministic scorer confirms the 14/100 result is stable. The plan.json / score.json on this branch (from a later gpt-4o run under image 0.3.0, planId `plan-c892e36ea9fb`) point at the same assessment; the report.md / report.html here therefore illustrate the correct plan structure for this assessment even though their footer `runId` no longer exists in the deployed 0.5.0 image's in-memory store.
- **Result:** 1 patch generated, 1 skipped.
  - `patches/001-pipeline-github-actions-arm64-job.patch` — adds `.github/workflows/arm64-build.yml` (docker buildx build for linux/arm64). **`git apply --check` clean against the pinned SHA.**
  - `wi-build-add-an-arm64-or-arm64ec-build-target` skipped ("no change produced"): the plan cited `build/add-arm64-target` but F1 didn't declare any build inputs, so `BuildConfigGenerator` had nothing to modify. Logged in `migration-result.json.skipped[]` — no silent failure.
- **`report.md` / `report.html`** are the deterministic Markdown / HTML rendering of that F2 plan. Regenerate against the current 0.5.0 planId with `curl https://.../api/migration-plans/{runId}/report.{md,html}` after a fresh POST if a byte-exact refresh is needed.

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
