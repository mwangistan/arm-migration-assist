---
mode: 'agent'
description: 'Audit knowledge/skills/catalog.json against actual runners and add implementationStatus + implementedBy per skill.'
---

# Task: Skill Catalog Audit

You are the Automation Engineer working on branch `chore/catalog-audit`.

## Goal

Add `implementationStatus` and `implementedBy` to every entry in
`knowledge/skills/catalog.json`, then bump `catalogVersion`. Every skill must
be classified honestly against actual C# source in this repo.

## Steps

1. Extend `backend/MigrationPlanner/contracts/SkillCatalogV1.schema.json`:
   - Add `implementationStatus`: `"runnable" | "declared-only" | "proposed"`.
     Required.
   - Add `implementedBy`: string. Required only when `implementationStatus`
     is `runnable`. Must be a fully qualified C# type or file path.
2. Walk each of the 22 skills in `knowledge/skills/catalog.json`. For each:
   - Search this repo for the executor. Confirm it end-to-end runs the skill.
   - If found, mark `runnable` and set `implementedBy` to the exact type or
     path.
   - If not, mark `declared-only`.
   - New skills you add this hackathon are `proposed` until code lands.
3. Bump `catalogVersion` to today's `YYYY-MM-DD.N` stamp.
4. Update the planner input assembly (`backend/MigrationPlanner/src/MigrationPlanner/`) so `workItems[]`
   are constrained to `runnable` skills; `declared-only` and `proposed`
   skills flow into `alternatives[]` or `proposedMissingSkills[]`.
5. Add a unit test that scans the catalog and fails if any `runnable` entry
   lacks an `implementedBy`.
6. Add a unit test that fails if the planner emits a work item whose skill
   is not `runnable`.
7. Open a PR titled `chore: skill catalog audit + implementationStatus` and
   request review from the Release Captain.

## Baseline mapping (verify each in code before committing)

- `assessment/*` — verify against `backend/Assessment/`.
- `validation/*` — verify against `backend/Validation/`.
- `build/add-arm64-target` — `BuildConfigGenerator` in
  `backend/AutomatedMigration/BuildConfiguration/`.
- `pipeline/github-actions-arm64-job`, `pipeline/matrix-fanout` —
  `PipelineGenerator` in `backend/AutomatedMigration/PipelineUpdates/`.
- `code/arch-conditional-cleanup` — `CodePatcher` + `GitHubModelsChatModel`
  in `backend/AutomatedMigration/CodeMigration/`.
- Everything else — expect `declared-only` unless you can point at a runner.

## Rules

- Do not mark a skill `runnable` on hope. Verify the executor.
- Do not delete a skill from the catalog to hide a gap. Downgrade to
  `declared-only`.
- Do not modify any other V1 schema.
