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
| `tests/` | xUnit unit tests and local Git/process/CLI integration tests. Fixtures live under the repository's ignored `artifacts/` directory and are removed after tests. |

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
```

Tests use injected providers/processes for platform-specific execution and exercise
real local Git identity checks, command capture, timeout handling, and CLI JSON
persistence. They do not call live models or claim to have run real Windows ARM64
or Docker workloads on the development machine.
