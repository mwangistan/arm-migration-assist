# ARM Migration Assist

ARM Migration Assist analyzes a public GitHub repository and produces an
evidence-based assessment of its readiness for Windows on Arm.

This repository implements **Feature 1: Repository Assessment Engine**. It
discovers repository technologies, dependencies, binaries, build signals, and
architecture-sensitive source code. The result is a deterministic JSON
document conforming to
[`RepositoryAssessmentV1.schema.json`](ArmMigrationAssist.Api/RepositoryAssessmentV1.schema.json),
plus a browser dashboard and printable report.

> **Feature boundary:** this project reports measured facts. It does not
> invent a readiness score, select a migration strategy, estimate effort, or
> claim that a repository will build successfully. Those planning decisions
> belong to Feature 2.

## Contents

- [What the application does](#what-the-application-does)
- [How an assessment works](#how-an-assessment-works)
- [Architecture](#architecture)
- [Prerequisites](#prerequisites)
- [Getting started](#getting-started)
- [Using the dashboard](#using-the-dashboard)
- [Using the REST API](#using-the-rest-api)
- [Using the CLI](#using-the-cli)
- [Understanding the results](#understanding-the-results)
- [Dependency analysis](#dependency-analysis)
- [Code compatibility analysis](#code-compatibility-analysis)
- [Build signals](#build-signals)
- [Configuration](#configuration)
- [Testing](#testing)
- [Safety and trust model](#safety-and-trust-model)
- [Known limitations](#known-limitations)
- [Feature 2 integration](#feature-2-integration)
- [Repository structure](#repository-structure)
- [Troubleshooting](#troubleshooting)
- [Roadmap](#roadmap)

## What the application does

Given a public GitHub URL such as
`https://github.com/microsoft/WindowsAppSDK`, ARM Migration Assist:

1. Validates that the input is a public HTTPS GitHub repository URL.
2. Creates or reuses a shallow local clone.
3. Records the exact commit, branch, and file inventory used for the run.
4. Detects languages, build systems, package managers, installers, CI, and
   Windows application technologies.
5. Discovers dependencies with Microsoft component-detection and built-in
   manifest parsers.
6. Inspects PE headers for checked-in executables, libraries, extensions, and
   drivers.
7. Verifies supported PyPI, npm, and NuGet packages against package registries.
8. Searches source for architecture-sensitive constructs such as x86
   intrinsics, inline assembly, architecture macros, P/Invoke, and pointer-size
   assumptions.
9. Maps findings to the versioned Feature 1 JSON contract.
10. Displays the result in a dashboard and print-friendly report.

The analysis is repeatable for the same repository commit and scanner version.
Run metadata such as the assessment ID and generation time changes between
runs.

## How an assessment works

The assessment pipeline consists of ordered `IAssessmentSkill`
implementations. Lower order values run first so later skills can consume facts
established earlier.

| Order | Skill | Responsibility |
|---:|---|---|
| 10 | Technology stack discovery | Languages, frameworks, build systems, package managers, project files, CI |
| 15 | Windows experience | Windows version, UI technology, installer, offline, accessibility, notifications, lifecycle |
| 20 | Build readiness | Explicit ARM64/Arm64EC targets, Windows ARM64 RIDs, CI and packaging signals, tests |
| 30 | Dependency scan | Component detection, manifests, local binaries, package registry inspection |
| 40 | Code compatibility | Architecture-sensitive source findings with file and line evidence |

`Order` is pipeline sequencing metadata, not a quality score or severity.

Each skill contributes facts to a shared `ReadinessManifest`. The
`AssessmentV1Mapper` then creates the external V1 document and
`EvidenceValidator` checks evidence references before the response is returned.

## Architecture

```text
Browser / CLI / API client
          |
          v
   POST /assess
          |
          v
RepositoryIngestionService
  - validates GitHub URL
  - shallow clone/cache
  - commit metadata
          |
          v
AssessmentService
  - executes ordered skills
          |
          +--> TechnologyStackDiscoverySkill
          +--> WindowsExperienceSkill
          +--> BuildReadinessSkill
          +--> DependencyScanSkill
          |      +--> component-detection
          |      +--> built-in manifest parsers
          |      +--> PEReader
          |      +--> package registries
          +--> CodeCompatibilitySkill
          |
          v
AssessmentV1Mapper + EvidenceValidator
          |
          v
RepositoryAssessmentV1 JSON
          |
          +--> Dashboard
          +--> Printable report
          +--> Feature 2 input
```

### Why deterministic scanners instead of an LLM

Binary architecture, package contents, build configuration, and source
locations are verifiable facts. An LLM should not guess them. Feature 1 uses
file parsing, package metadata, archive inspection, and PE headers. A later
planner may explain and prioritize these facts, but it should not replace them.

## Prerequisites

- Windows 10 or Windows 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Git
- Network access to public GitHub repositories
- Access to the configured package proxies
- Optional: Microsoft
  [component-detection](https://github.com/microsoft/component-detection)

The repository includes a Microsoft CFS-only `NuGet.config`. Package restore
does not directly use `nuget.org`.

## Getting started

From the repository root:

```powershell
dotnet restore .\ArmMigrationAssist.slnx
dotnet run --project .\ArmMigrationAssist.Api
```

Open:

```text
http://localhost:5285
```

The application serves both the static dashboard and the assessment API.

### Optional short workspace path

Some Windows repositories contain deeply nested paths. The ingestion service
enables Git long-path support for every command and uses short cache directory
names. If the repository still exceeds local path limits, configure a short,
writable workspace:

```powershell
$env:ARM_MIGRATION_WORKSPACE_ROOT = "C:\arm-ma"
dotnet run --project .\ArmMigrationAssist.Api
```

## Using the dashboard

1. Enter a public URL in the form
   `https://github.com/{owner}/{repository}`.
2. Select **ARM64 Native** or **Arm64EC** as planning context.
3. Start the assessment.
4. Review:
   - potential blockers and unresolved findings;
   - technology inventory;
   - explicit build and packaging signals;
   - dependency compatibility;
   - code findings and evidence;
   - scanner coverage and failures;
   - printable report and raw JSON.

### ARM64 Native versus Arm64EC

The selected target is currently recorded for downstream planning. Most Feature
1 scanners report target-independent repository facts, so selecting a different
target may produce the same findings. Feature 2 can use the target when choosing
a migration strategy.

### Repositories with no dependencies

A dependency count of zero can be correct. For example, `ww898/utf-cpp` is a
header-only library and can provide its functionality entirely as source.
The dashboard displays dependency resolution as **N/A** when no dependencies
are declared; this is not a scan failure.

## Using the REST API

### Endpoint

```http
POST /assess
Content-Type: application/json
```

### Request

```json
{
  "repoUrl": "https://github.com/ww898/utf-cpp",
  "target": "Arm64Native"
}
```

Valid target values:

- `Arm64Native`
- `Arm64EC`

### PowerShell example

```powershell
$body = @{
    repoUrl = "https://github.com/ww898/utf-cpp"
    target  = "Arm64Native"
} | ConvertTo-Json

$assessment = Invoke-RestMethod `
    -Uri "http://localhost:5285/assess" `
    -Method Post `
    -ContentType "application/json" `
    -Body $body

$assessment | ConvertTo-Json -Depth 20 |
    Set-Content -Path ".\assessment.v1.json"
```

Only public HTTPS GitHub URLs are accepted. Arbitrary Git URLs, local paths,
SSH URLs, and other hosts are rejected.

## Using the CLI

The same assessment pipeline can run without the web UI:

```powershell
cd .\ArmMigrationAssist.Api
dotnet run -- assess https://github.com/ww898/utf-cpp Arm64Native
```

Arm64EC example:

```powershell
dotnet run -- assess https://github.com/microsoft/WindowsAppSDK Arm64EC
```

The JSON document is written to standard output. Evidence validation failures,
if any, are written to standard error.

## Understanding the results

### Dependency statuses

| Status | Meaning |
|---|---|
| `ready` | Evidence shows an ARM64 or architecture-neutral artifact/source is available |
| `emulation-only` | Only x86/x64 Windows artifacts were identified |
| `unknown` | Available evidence is insufficient for a safe classification |
| `blocked` | The discovered artifact cannot be used for an ARM64 process or system path |

`unknown` is intentional. It means the scanner did not have enough trustworthy
evidence; it does not mean incompatible.

### Potential blockers

The dashboard groups `blocked`, `emulation-only`, and `unknown` dependencies
with high/critical code findings under **Potential blockers**. This is a
triage list, not a final migration verdict.

### Confidence

Confidence reflects evidence strength:

- PE machine headers are nearly authoritative.
- Successfully inspected package archives have high confidence.
- Deterministic heuristics have moderate confidence.
- Unresolved packages have lower confidence.

Confidence is not the percentage chance that migration will succeed.

### Evidence

Every dependency or code finding includes evidence such as:

- a manifest path;
- a local package archive;
- a binary and PE machine type;
- a source file and line;
- a registry package observation;
- an artifact-level observation when no specific file applies.

## Dependency analysis

### Discovery sources

The scanner combines:

1. checked-in PE binaries;
2. Microsoft component-detection dependency graphs;
3. built-in parsers for `packages.config`, MSBuild projects, `package.json`,
   and `vcpkg.json`;
4. bundled local `.nupkg` archives;
5. PyPI, npm, and NuGet registry information.

Results are deduplicated by ecosystem and package name. Inspected binaries and
more decisive classifications take precedence.

### PE binaries

Files such as `.dll`, `.exe`, `.pyd`, `.node`, and `.sys` are inspected with
`System.Reflection.PortableExecutable.PEReader`.

| PE machine | Classification |
|---|---|
| ARM64 (`0xAA64`) | Ready |
| x64 (`0x8664`) | Emulation-only |
| x86 (`0x14C`) | Emulation-only |
| ARM32 | Blocked |

Non-ARM64 kernel drivers are blocked because Windows cannot emulate kernel-mode
drivers.

### NuGet

For an exact NuGet package version, the scanner downloads the `.nupkg` through
Microsoft CFS and inspects it in memory. It does not restore the package,
extract it to disk, run install scripts, or evaluate MSBuild.

NuGet signals include:

- `runtimes/win-arm64` and Arm64EC runtime assets;
- x64/x86-only Windows runtime assets;
- ARM32 runtime assets;
- IL-only AnyCPU assemblies;
- PE binaries under `lib`, `tools`, and other package directories;
- portable C/C++ headers and source;
- bundled local `.nupkg` files.

`.nuspec` definitions authored by the analyzed repository are excluded from the
dependency list because they represent package outputs, not consumed
dependencies.

Conservative unknowns remain for inaccessible feeds, non-exact versions,
meta-packages, build-only packages without inspectable payloads, and packages
over the configured 100 MB limit.

### PyPI

Wheel filenames provide platform evidence:

- `*-none-any.whl` is architecture-neutral;
- `win_arm64` confirms a Windows ARM64 wheel;
- Windows x64/x86 wheels without ARM64 are emulation-only;
- source-only distributions remain unknown because native extensions may need
  an ARM64 toolchain and source changes.

### npm

The verifier checks:

- explicit `cpu` restrictions;
- platform-specific optional dependencies;
- ARM64 prebuilt packages;
- the absence of native platform packages.

Packages with no CPU restriction or native platform package are treated as
architecture-neutral JavaScript. Known native binding packages remain subject
to package-specific verification.

### Other ecosystems

- JVM bytecode is treated as architecture-neutral, but JNI dependencies may
  still need manual validation.
- vcpkg packages remain unknown until `arm64-windows` availability is verified.
- Cargo and Go source dependencies generally rebuild for ARM64 but can contain
  native crates, assembly, or cgo dependencies.
- CocoaPods targets Apple platforms and is not a Windows dependency path.

## Code compatibility analysis

The source scanner identifies evidence that commonly requires attention during
a Windows ARM64 port:

- SSE, SSE2, AVX, and other x86 intrinsics;
- inline x86/x64 assembly;
- architecture preprocessor branches;
- P/Invoke and native DLL boundaries;
- assumptions that pointers or handles fit in 32-bit integers;
- hard-coded architecture paths or platform identifiers.

These are findings, not guaranteed defects. For example, an x86 intrinsic may
be guarded by a correct architecture branch or have an existing NEON
implementation elsewhere.

### NEON

NEON is Arm's SIMD instruction set, broadly analogous to x86 SSE/AVX for
parallel arithmetic and data processing. A migration may:

- use compiler-portable vector abstractions;
- use Arm NEON intrinsics;
- use standard-library/compiler auto-vectorization;
- keep architecture-specific implementations behind tested compile-time
  branches.

The tool reports the source location and remediation direction; it does not
automatically rewrite SIMD algorithms.

## Build signals

The build scanner reports explicit repository configuration:

- ARM64 target/platform token;
- Arm64EC target;
- `win-arm64` runtime identifier;
- ARM64 CI job;
- ARM64 installer/package configuration;
- test presence.

**Not found does not mean incompatible.** Portable and header-only projects may
compile for ARM64 through the selected compiler toolchain without containing
the literal string `ARM64`.

## Configuration

| Variable | Purpose | Default |
|---|---|---|
| `ARM_MIGRATION_WORKSPACE_ROOT` | Clone/cache root | `ArmMigrationAssist.Api\.arm-ma` |
| `COMPONENT_DETECTION_PATH` | Explicit component-detection executable | Auto-discover under `tools` or `PATH` |
| `NUGET_FLAT_CONTAINER` | Authorized NuGet V3 flat-container base | Microsoft CFS flat container |
| `NPM_REGISTRY` | Preferred npm registry base | Microsoft PackageFeedProxy |
| `NPM_CONFIG_REGISTRY` | Standard npm registry override | Microsoft PackageFeedProxy |

### Microsoft package sources

Repository restore uses:

```text
https://packagefeedproxy.microsoft.io/nuget/v3/index.json
```

Assessment-time NuGet archive inspection uses:

```text
https://packagefeedproxy.microsoft.io/nuget/v3/flat2
```

The scanner does not fall back directly to `api.nuget.org`.

## Testing

Run all tests:

```powershell
dotnet test .\ArmMigrationAssist.Tests\ArmMigrationAssist.Tests.csproj
```

The suite covers:

- schema mapping and evidence integrity;
- repository URL validation;
- component-detection manifest parsing;
- PyPI and npm decision logic;
- NuGet AnyCPU, ARM64, x64/x86, ARM32, source-only, and meta-package behavior;
- CFS-only NuGet behavior;
- architecture-remediation mappings;
- regression behavior for missing explicit ARM64 build signals.

Warnings about CFS vulnerability metadata (`NU1900`) do not necessarily mean
package restore failed. Check the final restore/build exit code.

## Safety and trust model

The service analyzes untrusted public repositories. To reduce risk:

- repository input is restricted to public HTTPS GitHub URLs;
- Git operations use argument lists rather than shell interpolation;
- clones are shallow and cached;
- package archives are inspected without extraction or execution;
- package downloads have a size limit;
- registry verification has bounded concurrency and request timeouts;
- normal assessment does not run `dotnet restore` inside the target repository;
- normal assessment does not evaluate arbitrary MSBuild imports or custom
  build tasks;
- no repository build scripts, package scripts, or tests are executed;
- malformed files degrade to explicit unknowns instead of aborting the entire
  assessment.

The tool is a static inspector, not a secure sandbox. Run it under an account
with only the filesystem and network permissions it needs.

## Known limitations

- Only public GitHub repositories are accepted.
- The tool does not prove that a project compiles or runs on ARM64.
- `Arm64Native` and `Arm64EC` currently provide planning context rather than
  changing most scanner rules.
- Regex-based source discovery can produce false positives and does not fully
  understand C/C++ or C# semantics.
- P/Invoke targets are identified but their deployed DLL architecture may not
  be available in the repository.
- Private package feeds require an authorized flat-container endpoint.
- Dynamic or centrally computed package versions may not resolve statically.
- Meta-packages may have no directly inspectable payload.
- Component detection can report build-time and transitive dependencies that
  are not shipped with the application.
- Language statistics are file-extension and byte based; generated or vendored
  code can affect the profile.
- A missing explicit ARM64 token does not establish incompatibility.
- A ready dependency result does not prove its APIs or behavior work correctly
  on Windows on Arm.

## Feature 2 integration

Feature 2 should consume only schema-conformant V1 output and preserve evidence
references when producing recommendations.

Feature 2 can add:

- overall readiness scoring;
- blocker weighting and prioritization;
- ARM64 Native versus Arm64EC strategy selection;
- replacement recommendations;
- migration phases;
- effort and risk estimates;
- a comprehensive migration plan.

Feature 2 should not reinterpret an unknown as ready without new evidence.

## Repository structure

```text
.
|-- ArmMigrationAssist.Api/
|   |-- Assessment/
|   |   |-- CodeCompatibility/
|   |   |-- Contract/
|   |   |-- DependencyScanner/
|   |   `-- RepositoryDiscovery/
|   |-- Controllers/
|   |-- wwwroot/
|   |-- Program.cs
|   `-- RepositoryAssessmentV1.schema.json
|-- ArmMigrationAssist.Tests/
|-- tools/
|-- NuGet.config
|-- ArmMigrationAssist.slnx
`-- README.md
```

Runtime clone caches, downloaded component-detection binaries, build outputs,
and generated assessment JSON are intentionally excluded from Git.

## Troubleshooting

### A large repository fails with filename-too-long errors

Use a short workspace root:

```powershell
$env:ARM_MIGRATION_WORKSPACE_ROOT = "C:\arm-ma"
```

Restart the application and retry.

### NuGet packages remain unknown

Check:

1. the package has an exact version rather than an MSBuild expression;
2. the version is available through CFS;
3. the package is not a private/internal package;
4. the package is not a payload-free meta-package;
5. the package is below the inspection size limit.

Unknown means insufficient evidence, not failed ARM64 support.

### Direct `nuget.org` or npm access fails

Microsoft-managed devices may block direct public package registries. This
project uses the Microsoft package proxies by default. Do not add public
registries alongside CFS in the repository configuration.

### The dashboard reports no dependencies

This can be correct for a source-only or header-only repository. Verify the
detected package managers and project files. If no package manager is present,
dependency resolution is not applicable.

### Component detection is unavailable

Place the executable under `tools`, put it on `PATH`, or set:

```powershell
$env:COMPONENT_DETECTION_PATH = "C:\path\to\component-detection.exe"
```

The built-in manifest parsers still run when the external scanner is absent.

## Roadmap

- Resolve vcpkg `arm64-windows` availability.
- Replace regex-based C# analysis with Roslyn.
- Add a structured parser for deeper C/C++ semantic analysis.
- Resolve P/Invoke targets to deployed binary evidence.
- Distinguish shipped/runtime dependencies from build-only dependencies more
  precisely.
- Add target-specific Arm64EC compatibility rules.
- Hand schema-conformant assessments to the Feature 2 migration planner.
