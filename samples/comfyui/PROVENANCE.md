# ComfyUI — golden bundle provenance

Records exactly what produced the artifacts in this folder so the run can be
reconstructed inside the freeze window.

## Repo state used to produce this bundle

- **arm-migration-assist commit:** `3765849` (main after PRs #11 catalog audit, #14 name reconciliation)
- **Skill catalog version:** `2026-09-17.2` (from `knowledge/skills/catalog.json`)
- **Windows on Arm corpus version:** `2026-09-15.1`

## Feature 1 Assessment — local CLI against local worktree

- **Producer:** `arm-migration-assist-repository-discovery` (see `assessment.json.producer`)
- **Local clone:** `C:\temp\comfyui-387f98a` — `git -c core.autocrlf=false clone https://github.com/comfyanonymous/ComfyUI` then `git checkout 387f98aa2822f684b8597959a52a467d88cc4806`. Note: `core.autocrlf=false` is required on Windows to keep the working tree byte-identical to what git tracks; without it F1 rejects the worktree as dirty.
- **Invocation:** `dotnet run --project backend/Assessment/RepositoryDiscovery/RepositoryDiscovery.csproj --no-build -- C:\temp\comfyui-387f98a --output C:\temp\comfyui-assessment.json`
- **.NET SDK:** 9.0.318 (project targets `net8.0`)
- **Result:** schema-valid `RepositoryAssessmentV1`. Repository resolved to `master @ 387f98aa`. 39 dependencies, 27 code findings, 4 unknowns.

## `availableSkills[]` declared by F1 (post-reconciliation)

- `assessment/repository-discovery`
- `assessment/dependency-scan`
- `assessment/code-compatibility-scan`
- `pipeline/github-actions-arm64-job`

`build/add-arm64-target` is deliberately **not** declared: ComfyUI has no
Dockerfile / .csproj / .vcxproj at the pinned SHA. `code/arch-conditional-cleanup`
is deliberately not declared: the catalog marks it `declared-only`, so the F1
`SkillCatalog` gate drops it even though 27 code-findings would otherwise
trigger it.

## Feature 2 Planner — deployed API

- **Endpoint:** `https://ca-arm-migration-planner-api.delightfulcliff-b520a3d2.eastus2.azurecontainerapps.io`
- **Container App:** `ca-arm-migration-planner-api`
- **Resource group:** `rg-arm-migration-assist`
- **Deployed image tag at time of run:** `migration-planner-api:0.3.0` (post-reconciliation main).
- **Model provider (per response):** `fake / fake-planner / 0.1.0`. **NOT the intended Hosted / gpt-4o path.** The `MIGRATIONPLANNER_MODEL_PROVIDER` env var was reset during the redeploy and setting it to `Hosted` produced 500s (credential path to `foundry-arm-mig-assist.openai.azure.com` needs investigation). The Fake provider still emits schema-valid, catalog-form skill names, which is what F3 needs — but the plan narrative is canned rather than gpt-4o-drafted. Follow-up: fix the Hosted provider or grant the ACA managed identity the right role on the OpenAI endpoint.
- **Invocation:** `POST /api/migration-plans` with the on-disk `assessment.json`.
- **Elapsed:** ~2 s.
- **Result:** `runId = baeca60e2e19484abe72925257ce3968`, split into `plan.json` and `score.json` via `System.Text.Json` (raw JSON preserved).

## Feature 3 AutomatedMigration — local runner

- **Producer:** `AutomatedMigration.MigrationActionsRunner` at commit `3765849`.
- **Invocation:** `dotnet run --project backend/AutomatedMigration/AutomatedMigration.csproj --no-build -- C:\temp\comfyui-plan.json C:\temp\comfyui-387f98a C:\temp\comfyui-f3-output` — no `--publish`, dry-run only.
- **Result:** 1 patch generated, 1 skipped.
  - `patches/001-pipeline-github-actions-arm64-job.patch` — adds `.github/workflows/arm64-build.yml` (docker buildx build for linux/arm64). **`git apply --check` clean against the pinned SHA.**
  - `wi-build-add-an-arm64-or-arm64ec-build-target` skipped ("no change produced"): the plan cited `build/add-arm64-target` but F1 didn't declare any build inputs, so `BuildConfigGenerator` had nothing to modify. Correctly logged in `migration-result.json.skipped[]` — no silent failure.

## Score summary

- `overallScore`: **14 / 100**
- `band`: **`blocked-or-major-redesign`**
- `provisional`: **true**
- `recommendedPath` in plan: **`native-arm64`**

## Reports

- `report.md` (41.5 KB) and `report.html` (53.8 KB) fetched from
  `GET /api/migration-plans/{runId}/report.{ext}` on the deployed 0.3.0 image.
  Both include the `scoreDigest` footer and drop unknown-guidance IDs.

## Known gaps in this bundle

- **Fake provider, not gpt-4o.** See Model provider note above. Regenerate once
  the Hosted provider is fixed for a richer, LLM-drafted narrative.
- **`validation.json` is absent** until Feature 4 runs against the patched
  workspace.
- **F3 does not apply the patches** — F4 (Validation) or the Curator does that
  before running smoke tests.
