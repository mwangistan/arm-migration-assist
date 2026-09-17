---
mode: 'agent'
description: 'Render migration report (Markdown + HTML) for a given runId via the Planner report endpoint and commit under samples/<slug>/.'
---

# Task: Render report for a run

You are the Evidence Curator. Fetch the deterministic report for
`runId=${input:runId:Planner runId}` from the deployed Planner and commit both
formats under `samples/${input:slug:Sample folder slug}/`.

## Preconditions

- Planner Finisher's report endpoint is deployed. Verify by hitting
  `GET /api/migration-plans/${input:runId}/report.md` and seeing 200.
- `samples/${input:slug}/plan.json` and `score.json` already committed.

## Steps

1. Fetch Markdown:
   ```powershell
   Invoke-WebRequest `
     -Uri "https://<planner-endpoint>/api/migration-plans/${input:runId}/report.md" `
     -OutFile .\samples\${input:slug}\report.md
   ```
   Endpoint URL is in `/memories/repo/deployment.md`.
2. Fetch HTML the same way with `report.html`.
3. Verify each file is self-contained:
   - Markdown renders correctly in a preview.
   - HTML opens offline with inline CSS and no missing assets.
4. Confirm the footer contains a `scoreDigest` matching
   `samples/${input:slug}/score.json`. If not, fail the task and file a bug
   against Planner Finisher.
5. Confirm no work item in either report references a skill that is not
   `runnable` in the current catalog. If any does, fail the task and file a
   bug against Planner Finisher.
6. Redact any absolute file paths from the rendered output before committing.
7. Update `samples/${input:slug}/PROVENANCE.md` with the deployed image tag
   and catalog version that produced the report.
8. Open a PR titled `chore(samples/${input:slug}): migration report`.

## Rules

- Never regenerate the report by calling the LLM. The endpoint is stateless
  and deterministic — use it.
- Never edit the returned Markdown or HTML by hand. If it's wrong, file a
  bug.
