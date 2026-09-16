# Feature 3: Automated Migration Actions

Turns an approved **migration plan** into **reviewable ARM64 change files (diffs)**.
This component never edits the target repo in place and never applies changes — it
only generates patches for a human to review and merge.

## Where it sits in the pipeline

```text
Feature 1: Assessment   → facts, dependencies, blockers (with evidence)
Feature 2: Planner      → MigrationPlanV1  ← our input
        ↓
Feature 3: Migration Actions  → .patch / .diff files  ← our output
        ↓
Feature 4: Validation   → builds / tests the generated diffs
```

We consume `MigrationPlanV1` from the Planner. We do **not** clone repos or detect
stacks — that is Feature 1's job.

## What we read from the plan

The plan is a list of `workItems[]`. It tells us **what** to change, not the literal
diff. For each work item we use:

| Field | How we use it |
|-------|---------------|
| `agentOrSkill` | Selects which generator runs. Only work items targeting our skills are ours. |
| `objective` | What the change must accomplish. |
| `inputs` | Files the generator needs (e.g. `Dockerfile`, `pyproject.toml`, `*.csproj`). |
| `expectedOutputs` | What we must produce (e.g. "arm64 Dockerfile diff"). |
| `acceptanceTests` | Checks the generated diff must satisfy. |
| `sequence` + `dependencies` | Order to run work items in. |
| `evidenceIds` / `guidanceIds` | Echoed into our output so the report stays traceable. |
| `approvalRequired` (always `true`) | We generate only; a human approves before anything is applied. |

## Skills we own

We run a work item only when its `agentOrSkill` matches one of these:

| Skill | Story | Subfolder | Produces |
|-------|-------|-----------|----------|
| `build-config-generator` | 3.1 | `BuildConfiguration/` | ARM64 build/packaging diff (Dockerfile, `.csproj`, or `.vcxproj`) |
| `ci-pipeline-generator` | 3.2 | `PipelineUpdates/` | ARM64 CI job diff (GitHub Actions / Azure Pipelines) |
| `code-transformer` | 3.3 | `CodeMigration/` | One architecture-specific code diff, with rationale (stretch) |

### Build systems supported by `build-config-generator`

The target file comes from the work item's `inputs`; the generator picks the rule
by file type. Unsupported build systems produce no change (no guessing).

| Build file | Rule applied |
|-----------|--------------|
| `Dockerfile` | Add `--platform=linux/arm64` to the `FROM` line |
| `*.csproj` | Add `win-arm64` to `<RuntimeIdentifiers>` |
| `*.vcxproj` | Add `Debug\|ARM64` and `Release\|ARM64` project configurations |
| CMake / others | Not yet supported → returns no change |

## How it runs

```text
read MigrationPlanV1
  → keep workItems where agentOrSkill is one of our skills
  → sort by sequence (respect dependencies)
  → for each work item:
        input  = workItem.inputs + repo files
        output = a .patch that satisfies workItem.expectedOutputs
  → write patches to output/, tagged with evidenceIds / guidanceIds
  → stop. Do NOT apply — leave for human approval.
```

## The one rule

Every result is a `.patch` / `.diff` written to `output/`. No in-place edits, no
execution, no publishing. This satisfies the plan's "reviewable, reversible,
approval-gated" requirement and keeps the component simple.

## Priorities

1. **3.1 `build-config-generator`** — the P0; every reference repo needs one valid ARM64 build/config change.
2. **3.2 `ci-pipeline-generator`** — adds the ARM64 CI job.
3. **3.3 `code-transformer`** — one solid pattern transform if time allows.
