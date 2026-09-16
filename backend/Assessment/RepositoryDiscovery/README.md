# Feature 1 Repository Assessment

Implements and orchestrates Feature 1 stories 1.1 through 1.4: repository intake,
technology discovery, dependency scanning, and architecture compatibility
scanning. It accepts an anonymous public GitHub URL or a clean local Git clone
whose `origin` points to GitHub, then writes a `RepositoryAssessmentV1` JSON
artifact with stable assessment and evidence identifiers.

## Run the CLI

```pwsh
dotnet run --project backend/Assessment/RepositoryDiscovery/RepositoryDiscovery.csproj -- `
  https://github.com/owner/repository `
  --output artifacts/repository-assessment.json
```

A local clone can be supplied instead:

```pwsh
dotnet run --project backend/Assessment/RepositoryDiscovery/RepositoryDiscovery.csproj -- `
  C:\src\repository `
  --output artifacts/repository-assessment.json
```

Local clones must have no modified tracked files. Untracked files are not read.
This keeps the reported commit SHA aligned with the files being assessed.

## Run the API and dashboard

Start the API:

```pwsh
dotnet run --project backend/Assessment/RepositoryDiscovery/RepositoryDiscovery.csproj -- serve
```

The API listens at `http://localhost:5000` by default and exposes:

- `GET /api/health`
- `POST /api/assessments` with `{ "source": "https://github.com/owner/repository" }`

API intake accepts public GitHub URLs only. Local clone assessment remains a CLI
workflow. In another terminal, start the dashboard:

```pwsh
Set-Location frontend
npm install
npm run dev
```

Open `http://127.0.0.1:5173`. Vite proxies `/api` requests to the local API.

## What it discovers

- repository name, normalized public URL, commit SHA, default branch, and license
- languages and project types
- frameworks, build systems, and package managers
- installer and CI systems
- NuGet, npm, Python, vcpkg, Cargo, and Go dependency declarations
- checked-in PE and ELF binary architecture from validated headers
- architecture status backed by package or binary evidence
- P/Invoke, inline assembly, x86 SIMD, architecture conditionals,
  pointer-size-sensitive code, and dynamic native loading
- ARM64 and Arm64EC build targets
- ARM64 CI and packaging signals
- test-suite and conservative Windows experience signals
- scan coverage, malformed-manifest gaps, explicit unknowns, and reusable skills

The scanners use static repository evidence only. A dependency with no
repository-visible architecture signal remains `unknown`; Feature 1 does not
query package registries, execute builds, calculate readiness scores, or propose
migration strategy.

## Safety and privacy

- URL intake only accepts anonymous `https://github.com/owner/repository` URLs.
- Git credential helpers, prompts, hooks, system configuration, and submodule
  recursion are disabled for discovery commands.
- Only Git-tracked files are considered; `.git`, untracked files, and submodules
  are not scanned.
- Symbolic links, reparse points, unsafe paths, and oversized scan inputs are
  skipped and reflected in scan coverage.
- File reads are bounded, and build tools or repository code are never executed.
- Evidence contains repository-relative paths and fixed factual observations,
  never source text or local filesystem paths.
- API concurrency is bounded to two assessments and honors request cancellation.

## Test

```pwsh
dotnet test backend/Assessment/RepositoryDiscovery.Tests/RepositoryDiscovery.Tests.csproj `
  --configuration Release
```

The integration tests create temporary Git repositories, exercise the service,
CLI, API, dependency and code scanners, and validate output against
`MigrationPlanner/contracts/RepositoryAssessmentV1.schema.json`.