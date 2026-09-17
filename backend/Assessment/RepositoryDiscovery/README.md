# Feature 1 Repository Assessment

Implements and orchestrates Feature 1 stories 1.1 through 1.4: repository intake,
technology discovery, dependency scanning, and architecture compatibility
scanning. It accepts a GitHub URL or a clean local Git clone
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

| Method | Endpoint | Purpose |
|--------|----------|---------|
| `GET` | `/api/health` | Health, schema version, and capabilities |
| `GET` | `/api/contracts/repository-assessment/v1` | Versioned JSON Schema consumed by Feature 2 |
| `POST` | `/api/assessments` | Synchronous assessment for simple clients |
| `POST` | `/api/assessment-jobs` | Queue work and return status, event, and result links |
| `GET` | `/api/assessment-jobs/{jobId}` | Poll status and progress |
| `GET` | `/api/assessment-jobs/{jobId}/events` | Receive ordered SSE progress and terminal events |
| `GET` | `/api/assessment-jobs/{jobId}/result` | Read completed `RepositoryAssessmentV1` JSON |
| `DELETE` | `/api/assessment-jobs/{jobId}` | Cancel queued or running work |
| `POST` | `/api/auth/github/sessions` | Start local GitHub browser authentication |
| `GET` | `/api/auth/github/sessions/{sessionId}` | Poll authentication status |
| `DELETE` | `/api/auth/github/sessions/{sessionId}` | Cancel pending browser authentication |

Create an asynchronous assessment:

```http
POST /api/assessment-jobs
Content-Type: application/json

{"source":"https://github.com/owner/repository"}
```

The returned resource contains `statusUrl`, `eventsUrl`, and `resultUrl`. SSE
events have ordered IDs, a reconnect interval, 15-second keep-alives, phase,
percent, and terminal status. Clients may reconnect with `Last-Event-ID` or use
status polling. Completed results are the same schema-valid JSON emitted by the
CLI and synchronous endpoint. Jobs are memory-backed, bounded to 100 retained
records, and kept for up to one hour; durable consumers should persist the
completed result.

### Protected repositories

The dashboard first attempts anonymous access. If GitHub requires credentials,
the API starts Git Credential Manager's browser flow, the dashboard polls its
short-lived session, and the original assessment resumes automatically after
successful sign-in. The selected GitHub account must have repository access and,
where required, organization SSO authorization.

This flow requires Git Credential Manager (included with Git for Windows). It is
available only through loopback API requests. Credentials remain in the operating
system credential store; tokens, account details, and authenticated clone URLs
are never returned by the API or written to assessment JSON. The CLI remains
non-interactive and accepts anonymous URLs or an already available clean clone.

Local clone assessment remains a CLI workflow. In another terminal, start the
dashboard:

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
- checked-in Python extensions (`.pyd`) and assembly source (`.asm`/`.s`)
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

- URL intake accepts only credential-free
  `https://github.com/owner/repository` values; credentials are never accepted in
  URLs or request bodies.
- Anonymous Git commands disable credential helpers, prompts, hooks, system
  configuration, and submodule recursion. An authorized loopback session enables
  only Git Credential Manager for the single retry.
- Only Git-tracked files are considered; `.git`, untracked files, and submodules
  are not scanned.
- Symbolic links, reparse points, unsafe paths, and oversized scan inputs are
  skipped and reflected in scan coverage.
- File reads are bounded, and build tools or repository code are never executed.
- Evidence contains repository-relative paths and fixed factual observations,
  never source text or local filesystem paths.
- API concurrency is bounded to two assessments and honors request cancellation.
- Browser authentication and stored credential use are loopback-only.
- API responses use `Cache-Control: no-store`, including protected-repository
  status and results.
- Cross-record validation rejects duplicate evidence IDs, unresolved evidence
  references, and invalid coverage counts before publishing an assessment.

## Test

```pwsh
dotnet test backend/Assessment/RepositoryDiscovery.Tests/RepositoryDiscovery.Tests.csproj `
  --configuration Release
```

The integration tests create temporary Git repositories, exercise the service,
CLI, API, dependency and code scanners, and validate output against
`MigrationPlanner/contracts/RepositoryAssessmentV1.schema.json`.