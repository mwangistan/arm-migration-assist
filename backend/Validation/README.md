# Feature 4: Validation and Demo Experience

An approval-gated .NET 8 validation engine and local CLI. It consumes a **materialized,
clean Git working tree** produced by Feature 3 and records measured outcomes.
It does not apply patches, clone repositories, switch branches, push commits, create
pipelines, or claim readiness from a successful cross-build alone.

## Ownership and architecture

| Component | Responsibility |
|-----------|----------------|
| Feature 2 `MigrationPlanV1` | Owns `validationPlan`, its eight check categories, target devices, and work-item acceptance tests. |
| Feature 3 | Owns patches, build/pipeline changes, and optional clone/branch materialization. Patch files alone are not an execution target. |
| `BuildValidation/Contracts.cs` | Read-only Feature 2 subset, prepared command plan, approval, evidence, status, and report contracts. |
| `RepositoryInspector.cs` | Checks effective Git configuration, root, full commit SHA, branch, index entries/flags, every tracked file's raw blob hash against the pinned commit, and untracked files. Supports Git worktrees and detached HEAD. |
| `DeterministicPlanner.cs` | Conservative executable discovery, explicit bindings/smoke inputs, and manual/uncovered criteria. |
| `AiContracts.cs`, `FoundryValidationAi.cs`, `ValidationWorkflow.cs` | Injectable AI planning → approval boundary → deterministic execution → AI evidence analysis → AI coverage review, with an Azure Foundry chat-model implementation. |
| `ValidationExecutor.cs`, `ProcessRunner.cs` | Runner eligibility, approved command execution, bounded output capture, timeout/cancellation, proof parsing, dependency gating, and post-run repository verification. |
| `ScorecardBuilder.cs` | Deterministic criterion aggregation, overall status, and coverage gaps. |
| `Dashboard/ValidationDashboard.cs` | Versioned read model and JSON projection suitable for an API. No frontend pages or hosted web API are introduced. |
| `Api/` | ASP.NET Core wrapper/orchestrator for asynchronous plan approval and run execution. It references the validation engine and persists API-owned JSON metadata outside target repositories. **Interim standalone host**: the overall backend architecture consolidates all four features into a single ASP.NET Core project and Docker image (see the `soph/feature/repo-assessment` branch's `backend/ArmMigrationAssist.Api.csproj`). Once that shared project is merged into `main`, this project's endpoints should move into a `ValidationController` registered there, and this standalone `Api/` host/Dockerfile should be retired in favor of the shared one. |
| `tests/` | xUnit unit tests and local Git/process/CLI integration tests. Fixtures live under the repository's ignored `artifacts/` directory and are removed after tests. |
| `Api.Tests/` | TestServer/WebApplicationFactory coverage for API endpoints, queue lifecycle, recovery semantics, local-only middleware, and filesystem persistence. |

Reference demos remain in the top-level `samples/` folder. `ICiValidationObserver`
is an extension seam to **observe an existing CI run**, identified by repository,
exact commit, and run reference. There is no CI provider implementation or pipeline
provisioning. CI observations are not automatically promoted into trusted local results.
CI-specific requirements without observed evidence stay not-run.

## Workflow and approval

1. `ValidationWorkflow.PrepareAsync` inspects the repository and reads the migration
   plan. It discovers tracked `.csproj`, `.vcxproj`, and `Dockerfile` artifacts.
   Other filenames/build systems require explicitly proposed custom checks.
2. An optional `IValidationPlanner` receives repository context (including bounded
   build-artifact contents), the migration validation plan, acceptance tests, and the
   deterministic proposal. It can add custom commands, propose criterion bindings,
   and describe manual checks. It cannot modify the detected command templates.
3. A human or authenticated approval-owning application reviews the **complete**
   prepared plan: executable, argument array, relative cwd, environment overrides,
   timeout, dependencies, target surface, proof requirements, and criterion mappings.
   `PlanApproval` binds approved command IDs to a canonical SHA-256 fingerprint of
   the entire proposal, including repository identity and acceptance meanings.
   Empty/missing approvals execute no validation commands. Plan changes invalidate approval.
4. `RunAsync` freezes the input, verifies identity again, executes approved commands
   without implicit shell expansion, and collects evidence. Failed/unavailable
   prerequisites prevent dependent execution. A final repository check downgrades
   successes to inconclusive if the source tree or identity changed during the run.
5. The optional `IEvidenceAnalyzer` groups root causes and diagnoses ARM64 problems
   using actual command evidence. `ICoverageReviewer` recommends missing checks and
   highlights device, native ARM64, and x64 comparison gaps.
6. The report and dashboard keep deterministic results/evidence separate from AI
   diagnoses and recommendations. The CLI persists both as JSON.

## Azure Foundry model integration

The CLI enables all three AI stages when both environment variables are set. For
repeatable local execution, pass a `.runsettings` file:

```powershell
dotnet run --project .\backend\Validation\Validation.csproj -- `
  --settings .\backend\Validation\samples\dotnet-demo\validation.runsettings `
  plan <arguments...>
```

Authentication uses `DefaultAzureCredential` and the `https://ai.azure.com/.default`
scope. No API key or access token is stored in configuration or written to validation
evidence. If neither variable is set, the deterministic/manual workflow remains
available. Setting only one variable is treated as a configuration error.
Only `ARM_MIGRATION_FOUNDRY_ENDPOINT` and `ARM_MIGRATION_FOUNDRY_MODEL` are accepted
from the runsettings file.

Each AI request contains exactly two chat roles:

- `system`: the stage prompt, response contract, and evidence-first safety rules.
- `user`: the relevant inputs serialized as JSON and wrapped in explicit
  `BEGIN FILE`/`END FILE` boundaries with filenames and character counts.

Repository and evidence content is treated as untrusted data. The system prompt requires
a JSON-only response, which is deserialized into the existing typed AI contracts. The
workflow then validates referenced command, criterion, and evidence IDs plus confidence
ranges. Invalid or unavailable AI output cannot change deterministic results and causes
the existing deterministic/manual fallback.

Only read-only Git identity/discovery operations run during planning/verification;
validation/build/smoke commands require approval. Before index/worktree inspection,
effective Git configuration is checked and partial clones are rejected. Git fsmonitor,
replacement objects and submodule recursion are disabled. Configured Git filters are
not invoked: verification uses raw committed blob IDs and direct worktree byte hashing.
Submodules, nonregular tracked files, assume-unchanged/skip-worktree flags, and unmerged
entries are unsupported. Verification does not run `git status` or apply filters: the
index must match the pinned tree and **all tracked file bytes** must match its blobs.
Consequently, filter/newline/encoding-expanded checkouts are not supported; materialize
a byte-identical checkout (for example with `core.autocrlf=false`) before validation.
The engine never changes Git configuration or index flags to repair a target.
A fingerprint is an integrity binding, **not an authentication
system or sandbox**. The caller owns reviewer identity and authorization.

### Criteria are not inferred from prose

Validation checks use `validation:<check-id>` keys; acceptance tests use
`acceptance:<work-item-id>:<test-id>`, so test IDs can repeat across work items.
Original evidence/guidance IDs are preserved separately from new execution evidence.

Discovered commands initially satisfy only their narrow `discovered:<id>` criteria.
A build is not automatically evidence for every build-category or acceptance criterion.
Supply `PlanningOptions.mappings`, or review the AI planner's proposed mappings.
All mapped commands must pass for a criterion to pass. Container-build mappings also
require successful image-architecture inspection.

Unmapped functional, reliability, performance, power, offline, accessibility,
Windows-experience, target-device, and acceptance criteria remain manual/not-run.
All `discovered:target-*` criteria, including x64 comparisons, reject command mappings
and remain manual/not-run until typed device/measurement evidence is supported.
An OS architecture string or passing command does not establish Snapdragon/VM/device
identity or a comparable baseline.
Custom commands cannot satisfy the engine's native/container runtime coverage gates.
This version does not ingest manually asserted pass results.

## P0 execution support

| Detected input | Proposed validation | Constraints / honest gaps |
|----------------|---------------------|---------------------------|
| `.csproj` | `dotnet build <project> --configuration Release --runtime win-arm64` | This is cross-build evidence, not proof of native execution. SDK/workload availability is determined at execution. |
| `.csproj` with explicit `IsTestProject=true` or `Microsoft.NET.Test.Sdk` reference | One `dotnet test ... --runtime win-arm64 --framework <tfm> --logger trx;LogFileName=results.trx --results-directory <unique-per-command-evidence-path>` per explicit target framework | Requires a Windows ARM64 runner and a fresh TRX with nonzero executed tests, all passing, for **every** framework. Multi-target projects share a project criterion that requires all framework commands. Missing/malformed/stale/zero-test/partially skipped evidence cannot pass. Framework declarations must be literal and unambiguous; imported-only, conditional or computed frameworks leave tests manual/not-run. No MSBuild evaluation runs during discovery. |
| `.vcxproj` with declared `ProjectConfiguration` ending in `\|ARM64` | `msbuild <project> /t:Build /p:Configuration=<declared> /p:Platform=ARM64` | Windows runner and ARM64 C++ tools required; prefers a declared Release configuration. No ARM64 configuration means no guessed command. |
| Explicit `nativeSmokeChecks` | Supplied executable and argument array, dependent on its native build | Never inferred. Requires Windows ARM64 and verifies the supplied executable's PE machine is ARM64 (not x64 emulation). Scripts and ARM64EC are not treated as native ARM64 smoke binaries. |
| `Dockerfile` | `docker build --platform linux/arm64 ...`, then `docker image inspect` | Build context is the Dockerfile's directory. A pinned x64 base cannot silently pass: image metadata must say `linux/arm64`. More complex contexts need a reviewed custom plan. |
| Explicit `containerSmokeChecks` | `docker run --rm --platform linux/arm64 <unique-image-tag> <supplied-command>` after image inspection | **Linux ARM64 container validation only.** May run through emulation; not Windows UX, hardware performance, battery/power, or Windows on Arm readiness evidence. No smoke command is inferred from `ENTRYPOINT`. |

Docker images are uniquely tagged per prepared plan and are not automatically deleted;
cleanup would itself be a write-capable command requiring approval. The executor does
not install missing tools, repair the clone, enable emulation, or provision hardware.
Known unavailable SDK/workload/runtime/daemon errors become inconclusive; a missing
executable or incompatible host is not-run. Other nonzero exits are failures.
Later proof-read or artifact-hash errors add linked evidence diagnostics, preserving
established failures (and not-run outcomes). Only a would-be pass is downgraded to
inconclusive for unavailable proof.

## Local CLI usage

Run from the repository root. Use a separate materialized migration clone as the target.
Build outputs must already be ignored by that target's Git configuration; untracked
files (including unignored build output) prevent a clean-commit validation claim.
Keep proposal, approval, report, and evidence files **outside the target clone**,
or in a directory the target already ignores.

Create `validation-options.json` with an evidence directory:

```json
{
  "evidenceDirectory": "artifacts\\validation\\evidence"
}
```

Prepare a proposal and an approval skeleton (full SHA and branch are optional inputs;
the resolved full SHA and branch are always pinned in the proposal):

```powershell
dotnet run --project .\backend\Validation\Validation.csproj -- plan `
  .\migration-plan.json C:\work\migrated-repo .\validation-options.json `
  .\artifacts\validation\proposal.json .\artifacts\validation\approval.json `
  <full-commit-sha> <migration-branch>
```

The generated approval has an empty `approvedCommandIds` array. Inspect the proposal
and fill in **only the reviewed command IDs**, including required dependencies.
Do not change its fingerprint to accept unreviewed changes.

```json
{
  "planFingerprint": "<fingerprint from the generated approval>",
  "approvedCommandIds": ["dotnet-build-<discovered-id>"],
  "skippedCommands": {}
}
```

An optional `skippedCommands` object maps known, unapproved command IDs to explicit
waiver reasons. Skipped is not passed and does not make a required criterion validated.
Not approving a command, without an explicit waiver, means not-run.

### Dummy end-to-end data

When Feature 1–3 outputs are not available, create a disposable, clean Git target and
matching dummy inputs:

```powershell
.\backend\Validation\samples\dotnet-demo\Setup-Demo.ps1
```

The script writes only beneath `artifacts\validation-demo` by default and refuses to
overwrite an existing run. It creates a committed .NET 8 console repository, migration
plan, validation options, Foundry runsettings, and prints the exact `plan` and `run`
commands. The initial approval file still approves nothing: review `proposal.json` and
copy only the reviewed command IDs into `approval.json`.

An intentionally failing example is also available:

```powershell
.\backend\Validation\samples\dotnet-failure-demo\Setup-Demo.ps1
```

Its dummy project models an x64-only native dependency with no `win-arm64` asset. The
approved build exits nonzero with a stable diagnostic, allowing the workflow to
demonstrate failure evidence, a `validation-failed` scorecard, and Foundry root-cause
analysis. The failure is confined to the generated disposable repository.

For explicit mappings/smoke checks, add these optional fields to the options file,
then generate a new proposal using new output filenames and review it again:

```json
{
  "evidenceDirectory": "artifacts\\validation\\evidence",
  "mappings": [
    {
      "commandId": "dotnet-build-<discovered-id>",
      "criterionKeys": ["validation:vc-build", "acceptance:wi-build:at-build"]
    }
  ],
  "nativeSmokeChecks": [
    {
      "projectPath": "native/App.vcxproj",
      "executable": "native\\ARM64\\Release\\App.exe",
      "arguments": ["--smoke"],
      "criterionKeys": ["validation:vc-startup"]
    }
  ],
  "containerSmokeChecks": [
    {
      "dockerfilePath": "Dockerfile",
      "arguments": ["python", "-m", "app.smoke"],
      "criterionKeys": ["validation:vc-container"]
    }
  ]
}
```

Artifact paths used for matching (`projectPath`, `dockerfilePath`) must exactly match
the Git-relative paths shown in the proposal. Smoke executable paths are relative
to the repository root. No argument interpolation occurs.

Execute:

```powershell
dotnet run --project .\backend\Validation\Validation.csproj -- run `
  .\artifacts\validation\proposal.json .\artifacts\validation\approval.json `
  .\artifacts\validation\report.json .\artifacts\validation\dashboard.json
```

Existing output files are never overwritten. CLI exit codes: `0` means proposal
generation succeeded or the run is validated; `1` means validation-failed; `2` means
partial/not-validated or an input/infrastructure error. Inspect the JSON, not just
the CLI exit code. Re-running a test plan with an existing TRX yields inconclusive;
prepare a new plan for a fresh evidence directory.

## Local API usage

The ASP.NET Core API project is an orchestration layer over `ValidationWorkflow`; it
does not duplicate deterministic planning, approval fingerprinting, execution,
scorecard, evidence, or dashboard logic. It accepts work asynchronously: plan creation
runs planning immediately, run creation queues execution, and clients poll run status
before retrieving report/dashboard documents.

Run locally from the repository root:

```powershell
dotnet run --project .\backend\Validation\Api\Validation.Api.csproj
```

The default listen URL is loopback-only (`http://127.0.0.1:5084`), set once at the
top level via `WebApplicationBuilder`/`UseUrls` (there is no `Kestrel:Endpoints`
configuration section to override). Set the standard `ASPNETCORE_URLS` environment
variable (or `--urls`) to bind elsewhere, for example a container listening on all
interfaces at `http://+:8080`. The API also rejects non-loopback remote IPs by
default; TestServer requests with a null remote IP are allowed. Set
`ValidationApi:AllowNonLoopback=true` only behind authentication,
authorization, network isolation, and an execution-worker boundary appropriate for
running approved build/smoke commands. **This is unsafe on its own**: the API itself
implements no authentication or authorization, so enabling `AllowNonLoopback` without
adding your own auth in front of it exposes command execution to any reachable caller.
This API is not a production security boundary by itself.

Endpoint table:

| Method/path | Behavior |
|-------------|----------|
| `GET /healthz` | Returns `{ "status": "ok" }`. |
| `POST /api/v1/validation/plans` | Accepts `migrationPlan`, `target`, `options`, and optional `includeProposal`. Rejects a missing/null body, a missing `migrationPlan`/`target.path`/`options.evidenceDirectory`, a non-absolute `target.path`/`options.evidenceDirectory`, an evidence directory nested inside the target repository, or an evidence directory nested inside the API's own storage root — all with `400`. Otherwise runs safe planning, stores the prepared proposal (with no approval yet stored), and returns plan ID, fingerprint, status, and links. |
| `GET /api/v1/validation/plans/{planId}` | Returns persisted plan metadata and proposal data. |
| `GET /api/v1/validation/plans/{planId}/approval` | Returns the approval document, or a display-only skeleton (approves nothing) if none has been explicitly stored yet. The skeleton is never persisted by this call. |
| `PUT /api/v1/validation/plans/{planId}/approval` | Accepts approved command IDs plus optional skipped-command reasons and persists them as the plan's one explicit approval (an empty approved-command list is a valid, intentional choice). The server always binds the stored plan fingerprint; a supplied mismatched fingerprint is rejected with `409`. |
| `POST /api/v1/validation/plans/{planId}/runs` | Returns `409` if no approval has ever been explicitly stored (`PUT /approval` first; an explicit empty approval is allowed and satisfies this). Otherwise revalidates the stored proposal/approval, snapshots both into a new run directory (a later `PUT /approval` can never change an already-queued run), and queues execution. A plan has exactly one run in its lifetime: this returns `409` if any run — `queued`, `running`, or already terminal (`completed`/`failed`/`cancelled`) — was ever created for the plan, TRX/evidence proof paths being plan-scoped. Returns `503` if the run queue is full; the newly created run is still immediately marked `failed` (never left orphaned) and, because it was created, it still consumes the plan's one-run lifetime. There is no in-place retry: create and approve a new plan to run validation again. Otherwise returns `202` with run links. |
| `GET /api/v1/validation/runs/{runId}` | Returns `queued`, `running`, `completed`, `failed`, or `cancelled`, timestamps, summary, and sanitized error text. |
| `GET /api/v1/validation/runs/{runId}/report` | Returns `409` while `queued`/`running` (polling again may eventually succeed), a distinct terminal `409` if the run `failed` or was `cancelled` (a report will never become available), `404` for missing IDs/artifacts, then the `ValidationReport` JSON. |
| `GET /api/v1/validation/runs/{runId}/dashboard` | Same `409`/`404`/`200` semantics as `/report`, for the dashboard JSON. |

The API uses the same `ValidationJson` camel-case and kebab-case enum conventions as
the engine. Validation/client-input failures (malformed requests, unsafe/invalid paths,
approval/fingerprint mismatches) are returned as RFC7807 `ProblemDetails` with `400`,
`404`, `409`, or `503`. Every other failure — including any internal filesystem error —
is mapped to a generic `500 An unexpected validation API error occurred.` with no
message detail, so local absolute paths or other server internals are never echoed back
to a client (see `ExceptionMapping` in `Api/Program.cs`).

Example request shape:

```json
{
  "migrationPlan": { "...": "MigrationPlan payload" },
  "target": { "path": "C:\\work\\migrated-repo", "commitSha": null, "branch": null },
  "options": { "evidenceDirectory": "C:\\work\\validation-evidence" },
  "includeProposal": true
}
```

### API persistence, queue, and restart semantics

Configure storage with `ValidationApi:StorageRoot` or the environment variable
`ValidationApi__StorageRoot`. If unset, API metadata is stored beneath the process's
content root at `artifacts\validation-api` (no repository discovery/walk-up occurs).
When set explicitly, `StorageRoot` must be an absolute path; a relative value is
rejected rather than resolved against some discovered repository root. The storage
root must not be inside the target repository being validated. Evidence paths remain
controlled by the validation engine and the supplied `PlanningOptions.EvidenceDirectory`.

Storage layout:

```text
artifacts\validation-api\
  plans\<plan-id>\
    metadata.json
    proposal.json
    approval.json          (present only after the first PUT /approval)
    run-reservation.json   (permanent single-use reservation)
  runs\<run-id>\
    run.json
    plan-snapshot.json      (the prepared plan frozen at queue time)
    approval-snapshot.json  (the approval frozen at queue time)
    outputs\
      report.json
      dashboard.json
```

Plan and run IDs are generated server-side and restricted to safe hex IDs. JSON writes
are atomic temp-file writes followed by replace/move, storage access is guarded for
concurrent requests, and reparse-point/symlink paths are rejected where the engine's
path helpers apply. API endpoints never accept updates to proposal command definitions.

`approval.json` does not exist until the first `PUT /approval`; `CreatePlanAsync` never
writes it. `GET /approval` synthesizes a display-only skeleton in that case, but
`POST /runs` treats "no file" as "not yet approved" and returns `409` — an explicitly
stored empty approval is a different, allowed state that lets a run be queued (and
executes no commands).

`plan-snapshot.json`/`approval-snapshot.json` freeze exactly what a run will execute at
the moment it is queued. The worker always executes this snapshot, never the plan's
current (possibly since-changed) approval, so a later `PUT /approval` can never
retroactively change an already-queued or already-running run.

The store permanently writes `run-reservation.json` before saving snapshots and
publishes the queued `run.json` last, independently of request cancellation once
creation is admitted. Recovery never sees a partially written queued run. A failed
creation or quarantined run does not release its reservation; create and approve a
new plan rather than risking reuse of existing proof paths.

Because TRX/evidence proof paths are derived from the plan (not the run), a plan may
have at most one run for its entire lifetime, not merely one active run at a time.
Queueing a second run for a plan that already has one, whether that run is
`queued`, `running`, or already terminal (`completed`, `failed`, or `cancelled`),
always returns `409`. Reaching a terminal state does not free the plan for another
run. A run that a full queue immediately marks `failed` (`503` from `POST /runs`)
still counts as that plan's one run, so it also blocks any further run on the same
plan. To retry validation, create a new plan (`POST /plans`) and store a fresh
approval (`PUT /approval`) before queueing a run on it.

Runs execute one at a time through a bounded in-process channel. Configure capacity
with `ValidationApi:QueueCapacity` or `ValidationApi__QueueCapacity`; the default is
`100`. Queueing never blocks on channel capacity: if the channel is full, the API marks
the just-created run `failed` (so it is never left orphaned in `queued`) and returns
`503`. That run still consumed the plan's one-run lifetime, so retrying means
creating and approving a new plan once capacity frees up, not resubmitting the same
plan. Enqueueing is also never tied to request
cancellation, so a client aborting the HTTP request after a run is created cannot leave
it stuck in `queued` forever.

On API startup, queued runs are requeued (concurrently with the worker's read loop, so a
recovered backlog larger than the channel capacity cannot deadlock startup). Runs that
were `running` when the API stopped are marked `failed` with an interruption error
because their execution provenance cannot be assumed after restart. A single unreadable
or corrupt `run.json` is isolated to that run — it is logged, quarantined (renamed with
a `.corrupt-<timestamp>` suffix), and skipped — and never prevents recovery of every
other run or API startup.

Empty approval is allowed. It queues and completes a run in which no validation
commands are approved; the engine records command/criterion outcomes as not-run rather
than fabricating readiness.

### Container packaging

`Api/Dockerfile` builds a production image of Validation.Api. The build context is
`backend/Validation` (not `backend/Validation/Api`), because `Validation.Api.csproj`
has a `ProjectReference` to the sibling `../Validation.csproj` engine project:

```powershell
docker build -f backend/Validation/Api/Dockerfile -t validation-api:local backend/Validation
docker run --rm -p 127.0.0.1:8080:8080 `
    -e ValidationApi__AllowNonLoopback=true `
    -e ValidationApi__StorageRoot=/data/validation-api `
    -v validation-api-storage:/data/validation-api `
    validation-api:local
```

This local example binds only the host loopback interface. In Azure, enable
non-loopback access only behind authenticated,
authorized ingress. The image leaves `AllowNonLoopback` false by default.

The build needs NuGet access. If your environment requires a configured package
mirror, pass a NuGet configuration with BuildKit
`--secret id=nuget_config,src=<path-to-NuGet.Config>`; it is mounted during restore
and is not copied into image layers. Do not add credentials or feed configuration
to the source tree. Normal public NuGet builds do not need this option.

After building, run `pwsh -File backend\Validation\Api.Tests\ContainerSmoke.ps1
-Image validation-api:local` from the repository root. The script exercises the real
container over HTTP, an explicitly approved build, volume persistence across container
replacement, and the non-loopback gate. It removes its uniquely named containers and
test volume on exit.

The image is a two-stage build: an SDK stage restores/publishes the app, and the
final stage is also based on the .NET SDK (not just the ASP.NET runtime). This is a
deliberate choice, not an oversight: `ValidationExecutor`/`ProcessRunner` invoke
engine-approved commands (`dotnet build`/`dotnet test`, `msbuild`, `git`, native
executables) as child processes of the running API itself, at request time — not only
while building the image. Dropping to the slimmer aspnet runtime for the final stage
would let the API host itself but would break every approved command that needs the
`dotnet`/`msbuild` toolchain once a run actually executes inside the container. `git`
is installed for `GitRepositoryInspector`, and `curl` for the `HEALTHCHECK` below.

This makes the container **Linux-only** for approved-command purposes:

- Available: the .NET SDK and Git. The default planner emits `win-arm64` .NET
  cross-builds; these can build on Linux when project dependencies support it, but are
  not Windows ARM64 runtime evidence. Approved custom Linux commands may also execute.
- Does not work here: `.vcxproj`/MSBuild `ARM64` Windows builds and native Windows
  ARM64 smoke executables — those require a Windows ARM64 runner/toolchain and must
  keep running there, not inside this Linux image.
- Not included: a Docker CLI/daemon. `containerSmokeChecks` and Dockerfile-based
  validation commands need `docker build`/`docker run` against a Docker daemon; this
  image intentionally does not bundle one (mounting the host daemon socket would hand
  this API host-level control) and reports those commands not-run/inconclusive.

Image contract:

| Setting | Value |
|---------|-------|
| Listen | `ASPNETCORE_URLS=http://+:8080` (overrides the loopback-only in-process default; see "Local API usage" above) |
| Exposed port | `8080` (`EXPOSE 8080`) |
| Health probe | `HEALTHCHECK` runs `curl -fsS http://127.0.0.1:8080/healthz`; matches `GET /healthz` |
| Storage | `ValidationApi__StorageRoot=/data/validation-api`, pre-created, mountable, owned by the container's non-root user |
| User | Fixed non-root UID/GID `10001` (`validation-api`), not root, not an image-default `app` user |

A host/orchestrator volume or Azure Files mount at `/data/validation-api` must grant
read/write to UID/GID `10001` (or the equivalent `securityContext`/`fsGroup`), since
the container never runs as root.

#### Generic Azure Container Apps / App Service settings

Azure is **not** deployed by this change; the following is configuration guidance for
whoever deploys the image, not an executed deployment:

- **Port**: configure the target/container port as `8080` (Container Apps ingress
  `targetPort`; App Service classic custom containers `WEBSITES_PORT=8080`, or the
  container port field for sidecar-enabled apps); ingress and any health
  probe should use `8080`, matching `EXPOSE 8080`/`ASPNETCORE_URLS`.
- **Health probes**: point liveness/readiness (and Container Apps' startup probe) at
  `GET /healthz` on port `8080`.
- **Persistent storage**: mount a supported durable volume (for example Azure Files) at
  `/data/validation-api` (API plan/run metadata) and at whatever absolute container
  paths you choose for target repositories and evidence directories — for example
  `/mnt/repositories` and `/mnt/evidence` — then pass those exact absolute paths as
  `target.path`/`options.evidenceDirectory` in API requests. Grant the mounted
  volumes read/write access for UID/GID `10001`.
- **Single replica / no overlapping revisions**: the run queue is an in-process,
  in-memory `Channel` and run-record writes use an in-process semaphore, neither of which is
  shared or coordinated across instances. Scale to exactly one running
  replica/revision at a time (Container Apps: `minReplicas`/`maxReplicas` = 1, avoid
  overlapping revisions even if one receives no traffic, since workers start without
  requests; App Service:
  disable auto-scale-out beyond one instance). Multiple concurrent instances would
  each recover/queue runs independently and could race on the same plan/run files.
- **Non-loopback exposure**: set `ValidationApi__AllowNonLoopback=true` only once
  external requests are actually gated by Azure-side authentication/ingress
  restrictions (e.g. Container Apps ingress IP restrictions, Easy Auth/App Service
  authentication, a private endpoint, or an API Management/front-door layer) — the
  API itself still implements no authentication or authorization (see "Local API
  usage" above).
- **Foundry auth**: if AI stages are enabled, enable system-assigned or user-assigned managed identity and
  `DefaultAzureCredential` (already how `FoundryValidationAiClient` authenticates; no
  API key/secret is required or accepted). Grant that identity only the minimum
  Azure AI Foundry RBAC needed to call the configured deployment. Set only nonsecret
  values as configuration/environment: the Foundry endpoint, the deployment/model
  name, and (for a user-assigned identity) its client ID
  (`AZURE_CLIENT_ID`, read by `DefaultAzureCredential`) — never
  an API key or connection string.
- **Target repositories are local, not remote URLs**: `target.path` is always an
  absolute path inside the container's filesystem. This API does not clone arbitrary
  Git URLs and does not translate host paths to container paths; another trusted
  component is expected to clone/mount the repository at that absolute path before
  calling this API (e.g. an init container, a sidecar, or an Azure Files/volume mount
  populated out-of-band). Treat that mounting component, not this API, as the
  boundary that decides which repositories are reachable.
- **No auth in code**: this API intentionally implements no authentication or
  authorization of its own in this change; any exposure beyond loopback must be
  fronted by the Azure-side controls above.

Use `ValidationApi__StorageRoot=/data/validation-api` as an environment setting;
explicit storage paths must be absolute. Startup uses the content root for local
defaults and never searches for a source checkout. Container filesystems are ephemeral:
without durable mounts, plan approvals, run snapshots, reports, and external proof
files are lost on replacement. Persist evidence separately from API storage and target
repositories. Provision repository ownership/permissions for UID 10001; Git must trust
the checkout and it must be clean at the pinned commit. Do not globally disable Git's
ownership checks. Build outputs must be ignored by the target repository. Do not let
another component mutate a checkout while a run uses it.

`/healthz` is a process-liveness endpoint, not a guarantee that a command can execute
or that recovery has drained. Configure Azure probes explicitly; platforms need not
honor the image's Docker `HEALTHCHECK`. Keep the internal HTTP port at 8080 (terminate
TLS at the restricted Azure ingress). Stopping an old instance before starting its
replacement may require downtime; a shared volume alone does not enable safe scale-out.

### API Foundry configuration

The API never accepts Foundry endpoint or model values per request. Configure AI once
at process startup either with the same environment variables used by the CLI:

```powershell
$env:ARM_MIGRATION_FOUNDRY_ENDPOINT = "https://<foundry-endpoint>"
$env:ARM_MIGRATION_FOUNDRY_MODEL = "<deployment-name>"
dotnet run --project .\backend\Validation\Api\Validation.Api.csproj
```

or with application configuration:

```json
{
  "ValidationApi": {
    "Foundry": {
      "Endpoint": "https://<foundry-endpoint>",
      "DeploymentName": "<deployment-name>"
    }
  }
}
```

If neither source is present, deterministic/manual no-AI behavior is used. If only one
Foundry value is set, startup fails rather than silently changing validation behavior.

## Evidence and statuses

Command evidence records executable/arguments, absolute cwd, effective environment,
start/end timestamps, nullable exit code, stdout/stderr, start/timeout/cancellation
details, and file-path/SHA-256/length references for TRX artifacts. Decision evidence
also explains commands that did not run. Repository verification evidence anchors
the results to the requested working tree and commit. Stdout/stderr are retained
inline in the persisted report up to 1 Mi characters **per stream**, with truncation
marked; both streams are drained to avoid pipe deadlocks. Timeouts kill the started
process tree. Keep the report and its external TRX files together.

Only an allowlist of OS/toolchain environment variables is inherited; explicit
overrides are applied and recorded, with credential-like keys redacted in evidence.
Approval files contain the exact overrides. Avoid embedding secrets in arguments or
overrides: process logs and artifact contents are not guaranteed secret-free.
Choose storage permissions/retention and AI-provider data policy accordingly.

| Criterion/command status | Meaning |
|--------------------------|---------|
| `passed` | Approved deterministic execution and required proof succeeded. |
| `failed` | A nonzero exit or failed test/architecture assertion, with evidence. |
| `not-run` | No mapping, no approval, missing runner/tool, blocked prerequisite, or no execution. |
| `inconclusive` | Execution/provenance/proof is incomplete, timed out, cancelled, stale, skipped in part, or unavailable. |
| `skipped` | Explicit waiver with a reason; never a successful validation. |

Failed dominates criterion aggregation, then inconclusive. An all-passed mapping
passes; an entirely unrun or skipped mapping keeps that status. Mixed passed/unrun/
skipped evidence is inconclusive. No results or no criteria ever imply a pass.

| Overall status | Rule |
|----------------|------|
| `validation-failed` | At least one criterion failed. |
| `validated` | Nonempty criterion set, every criterion passed. |
| `partially-validated` | Some criteria passed, others remain unvalidated; none failed. |
| `not-validated` | No criterion passed and none failed. |

The scorecard uses counts rather than an invented readiness percentage. `validated`
means **the approved scope**, not universal Windows on Arm readiness. Coverage notes
still identify absent Windows runtime evidence, unrequested x64 comparisons, and
container-only scope. They do not imply measurements have taken place.

## AI extension and guardrails

Pass implementations of `IValidationPlanner`, `IEvidenceAnalyzer`, and
`ICoverageReviewer` to `ValidationWorkflow`; the CLI intentionally uses no-AI mode.
Each interface has a typed request/response in `AiContracts.cs` and a cancellation token.
For example, an API composition root can construct:

```csharp
var processes = new LocalProcessRunner();
var repositories = new GitRepositoryInspector(processes);
var workflow = new ValidationWorkflow(
    repositories, processes, myPlanner, myEvidenceAnalyzer, myCoverageReviewer);
var proposal = await workflow.PrepareAsync(migration, target, options, cancellationToken);
// Return the proposal to an authenticated reviewer; do not let an AI produce approval.
var report = await workflow.RunAsync(proposal, humanApproval, cancellationToken);
var dashboardJson = ValidationDashboard.FromReport(report).ToJson();
```

Provider adapters own endpoint policy, authentication, deadlines, retries, prompt
construction, and structured-output conversion. Repository content and command output
are untrusted data, not instructions. Supply only data permitted at the configured
endpoint. No existing repository-wide AI abstraction existed, so these interfaces
are local to Feature 4 rather than introducing shared infrastructure.

Guardrails enforced by orchestration:

- AI cannot approve commands or set deterministic statuses.
- Planner output must reference known criteria/commands and valid bounded commands,
  stay within repository-relative cwd boundaries, and form ordered, acyclic dependencies.
  Paths through symbolic links/reparse points are rejected.
- AI custom commands remain unclassified: they cannot masquerade as detected native
  ARM64 runtime coverage. Detected test/architecture proof requirements cannot be removed.
- AI input/output is snapshotted, preventing provider-side collection mutations from
  changing deterministic results or the approved plan.
- Diagnoses carry rationale, finite `[0,1]` confidence, command IDs, and nonempty
  references to actual captured evidence. Coverage recommendations cannot reference
  nonexistent criteria/evidence. Invalid responses are discarded.
- Missing/failing/invalid providers produce stage notices and deterministic/manual
  fallbacks, never fabricated analysis, a status upgrade, or an automatic retry command.
- Missing ARM64 hardware, tests, CI, or baseline measurements remain explicit gaps.

## Build and test

```powershell
dotnet build .\backend\Validation\Validation.csproj --nologo
dotnet test .\backend\Validation\tests\Validation.Tests.csproj --nologo
dotnet test .\backend\Validation\Api.Tests\Validation.Api.Tests.csproj --nologo
```

Tests use injected providers/processes for platform-specific execution and exercise
real local Git identity checks, command capture, timeout handling, and CLI JSON
persistence. They do not call live models or claim to have run real Windows ARM64
or Docker workloads on the development machine.
