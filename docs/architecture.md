# Architecture

The workflow follows the six stages in the spec:

1. **Connect** — provide a GitHub URL or local path and pick a target (ARM64 native or Arm64EC).
2. **Assess** — discover languages, dependencies, build system, and architecture-specific code.
3. **Plan** — score readiness and recommend a migration strategy.
4. **Transform** — generate reviewable build, pipeline, and code changes.
5. **Validate** — build, run checks/tests, and capture pass/fail evidence.
6. **Package** — export the report, diffs, and decision log.

## Where each stage lives

| Stage | Folder |
|-------|--------|
| Assess | `backend/Assessment/` |
| Plan | `backend/MigrationPlanner/` |
| Transform | `backend/AutomatedMigration/` |
| Validate | `backend/Validation/` |
| Experience (Connect + view results) | `frontend/` |

Keep the boundaries simple: assessment code produces facts with file evidence,
the planner scores and recommends, automated migration produces reviewable patches,
and validation records measured build/test outcomes.
