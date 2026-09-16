# ARM Migration Assist — Feature 1: Repository Assessment Engine

AI-assisted Windows on Arm readiness assessment. This project implements **Feature 1**
of the ARM Migration Assist hackathon plan: the deterministic **Repository Assessment
Engine** that turns a GitHub URL into an evidence-based readiness manifest.

> Design principle (from the plan): **deterministic before generative**. Feature 1 is
> pure scanners and PE-header inspection — no AI. Feature 2 (AI Migration Planner)
> consumes this manifest later.

## User stories implemented

Organized to mirror the `mwangistan/arm-migration-assist` repo layout (`backend/Assessment/...`).
Each deterministic scanner is exposed as a reusable **`IAssessmentSkill`**; the orchestrator
(and, later, an AI agent) invokes them through that uniform contract.

| Story | Capability | Folder / type |
|-------|-----------|---------------|
| 1.1 | Repository Ingestion (shallow clone + cache + metadata) | `Assessment/RepositoryDiscovery/RepositoryIngestionService` |
| 1.2 | Technology Stack Discovery (languages, build systems, package managers, CI) | `Assessment/RepositoryDiscovery/TechnologyStackDiscoverySkill` |
| — | Build-readiness (ARM64 targets, win-arm64 RIDs, arm64 CI jobs) | `Assessment/RepositoryDiscovery/BuildReadinessSkill` |
| 1.3 | Dependency Scanner (NuGet/npm/vcpkg manifests + **PE machine header** of every binary) | `Assessment/DependencyScanner/DependencyScanSkill` |
| 1.4 | Architecture Compatibility Scanner (SSE/AVX intrinsics, inline asm, arch macros, P/Invoke, pointer-size) | `Assessment/CodeCompatibility/CodeCompatibilitySkill` |

Every finding is linked to **evidence** (file + line, or binary path + PE machine type) and
carries a **stable deterministic `id`** so Feature 2 can reference and dedupe it.

### Why skills, not LLM "agents"

The plan labels these "agents," but its architecture principle is **deterministic before
generative**: scanners detect *facts*, AI *explains/prioritizes* them (Feature 2). An LLM must
never guess a binary's architecture, so Feature 1 is deterministic code exposed as skills.

### The manifest contract (what Feature 2 consumes)

`ReadinessManifest` (schema `1.0`) = `repository` + `target` + `technology` + `buildReadiness`
+ `dependencies[]` + `architectureFindings[]`. Facts only — scoring is Feature 2's job (Story 2.1).

## Run

Package restore is routed through Microsoft's CFS NuGet proxy by the repository-root
`NuGet.config`; no direct `nuget.org` package source is configured.

```powershell
cd ArmMigrationAssist.Api
dotnet run                 # Web dashboard + REST API on http://localhost:5285
dotnet run -- assess https://github.com/nothings/stb Arm64Native   # CLI, prints JSON manifest
```

Open `http://localhost:5285` to use the web dashboard. It accepts a public GitHub repository
URL, displays the technology profile, dependency compatibility matrix, potential blockers,
architecture-specific code findings, build signals, scanner coverage, and unresolved questions.
The Report tab provides a print-friendly Feature 1 assessment and the raw schema-conformant JSON
can be downloaded for Feature 2.

The dashboard deliberately does not invent an overall readiness score or migration strategy.
Those are Feature 2 outputs in the project plan; Feature 1 presents deterministic facts and
evidence only.

Repository clones are cached under `.arm-ma` and Git long-path support is enabled for each
operation. Set `ARM_MIGRATION_WORKSPACE_ROOT` to move the cache elsewhere; on Windows, prefer
a short writable path such as `C:\arm-ma` for repositories with deeply nested files.

## Assess a repository

```powershell
$body = @{ repoUrl = 'https://github.com/nothings/stb'; target = 'Arm64Native' } | ConvertTo-Json
Invoke-RestMethod -Uri 'http://localhost:5285/assess' -Method Post -Body $body -ContentType 'application/json'
```

`target` accepts `Arm64Native` or `Arm64EC`.

### Response shape (`ReadinessManifest`)

- `repository` — runId, commitSha, branch, fileCount, cache status
- `technology` — languages (by bytes), buildSystems, packageManagers, projectFiles, hasExistingCi
- `dependencies[]` — name, version, source, **machine** (ARM64/x64/x86), **classification**
  (`Arm64Ready` / `EmulationOnly` / `Unknown` / `Blocked`), evidencePath, notes
- `architectureFindings[]` — category, file, line, snippet, severity

## How dependency classification works

- **Binaries** (`.dll/.exe/.pyd/.node/.sys`): read `PEHeaders.CoffHeader.Machine` — the
  authoritative signal. `0xAA64`→ARM64 (ready), `0x8664`/`0x14C`→x64/x86 (emulation-only),
  `.sys` non-ARM64 → blocked (drivers can't be emulated).
- **NuGet**: exact package versions are downloaded as `.nupkg` archives (without extraction or
  execution). Windows RID assets under `runtimes/` are classified as ARM64-ready, emulation-only,
  or ARM32-blocked. Packages without Windows runtime assets are inspected with `PEReader`; this
  includes build tools under `tools/`, not only assemblies under `lib/`. IL-only managed assemblies
  are reported as AnyCPU, while portable C/C++ source/header packages are ARM64-ready source.
  Locally bundled `.nupkg` files are inspected directly, and `.nuspec` definitions authored by the
  repository are excluded because they are outputs rather than consumed dependencies.
  Meta-packages, unresolved versions, inaccessible private feeds, build-only packages, and packages
  over 100 MB remain `Unknown`. Set
  `NUGET_FLAT_CONTAINER` to an authorized NuGet V3 flat-container base when a private or custom
  package source is required. The scanner uses CFS by default and does not fall back directly to
  `api.nuget.org`.
- **vcpkg**: manifests are parsed and flagged `Unknown` with a note to verify the
  `arm64-windows` triplet.
- **npm**: pure JS treated as architecture-neutral; packages with native bindings
  (node-gyp / prebuilt `.node`) flagged for `win32-arm64` prebuild verification.

## Next steps (backlog)

- Registry verification of vcpkg `arm64-windows` triplets.
- Roslyn-based P/Invoke resolution (target DLL architecture) instead of regex.
- Readiness scoring (Feature 2, Story 2.1) computed from these findings.
