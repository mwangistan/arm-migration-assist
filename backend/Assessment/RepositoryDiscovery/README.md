# Repository Discovery

Implements Feature 1 stories 1.1 (repository intake) and 1.2 (technology
discovery). It accepts an anonymous public GitHub URL or a clean local Git clone
whose `origin` points to GitHub, then writes a `RepositoryAssessmentV1` JSON
artifact.

## Run

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

## What it discovers

- repository name, normalized public URL, commit SHA, default branch, and license
- languages and project types
- frameworks, build systems, and package managers
- installer and CI systems
- ARM64 and Arm64EC build targets
- ARM64 CI and packaging signals
- test-suite and conservative Windows experience signals
- scan coverage, explicit unknowns, and the reusable discovery skill

Dependency compatibility and architecture-specific source analysis are owned by
stories 1.3 and 1.4. Repository discovery therefore emits empty `dependencies`
and `codeFindings` arrays and records both gaps in `unknowns` instead of guessing.

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

## Test

```pwsh
dotnet test backend/Assessment/RepositoryDiscovery.Tests/RepositoryDiscovery.Tests.csproj `
  --configuration Release
```

The integration tests create temporary Git repositories, exercise the service
and CLI, and validate output against
`MigrationPlanner/contracts/RepositoryAssessmentV1.schema.json`.