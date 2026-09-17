---
mode: 'agent'
description: 'Run Feature 3 AutomatedMigration against a committed plan.json and produce the patch bundle + migration-result.json under samples/<slug>/.'
---

# Task: Emit patch bundle for a sample

You are the Automation Engineer. You will run
`backend/AutomatedMigration/` against a committed `MigrationPlanV1` and
produce a patch bundle under `samples/${input:slug:Sample folder slug (e.g. comfyui)}/`.

## Preconditions

- `chore/catalog-audit` is merged. Every emitted work item's `agentOrSkill`
  is `runnable` per `knowledge/skills/catalog.json`.
- `samples/${input:slug}/plan.json` exists and is a valid `MigrationPlanV1`.
- The pinned upstream repo is cloned locally at the SHA recorded in
  `samples/${input:slug}/SOURCE.md`.

## Steps

1. Run the F3 runner with the committed plan as input:
   ```powershell
   dotnet run --project .\backend\AutomatedMigration\AutomatedMigration.csproj `
     -- --plan-file .\samples\${input:slug}\plan.json `
        --repo-root <local-clone-path> `
        --out .\samples\${input:slug}\
   ```
2. Confirm the runner emits:
   - `samples/${input:slug}/patches/<sequence>-<slug>.diff` per work item.
   - `samples/${input:slug}/migration-result.json`.
3. Do **not** use `--publish`. No git commits into the sample repo.
4. For each patch, sanity-check it applies cleanly:
   `git apply --check <patch>` against the pinned clone.
5. If any work item fails, ensure `migration-result.json` records the
   failure honestly with a rationale. Do not retry to hide the failure.
6. Redact any absolute local paths from emitted diffs and JSON.
7. Update `samples/${input:slug}/PROVENANCE.md` with the F3 commit SHA and
   catalog version used.
8. Open a PR titled `chore(samples/${input:slug}): F3 patch bundle`.

## Rules

- Never mark a failing skill as succeeded. Downgrade to review-only.
- Never emit a patch referencing a file not present in the pinned source.
- Never publish a PR to the upstream sample repo.
- Never commit secrets extracted from CI files. Redact tokens.
