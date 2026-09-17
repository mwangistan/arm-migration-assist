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
