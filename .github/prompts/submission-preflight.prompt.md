---
mode: 'agent'
description: 'Pre-submission checklist. Verifies deployed API health, sample bundles, submission assets, and Innovation Studio links before Sept 21.'
---

# Task: Submission preflight

You are the Release Captain. Run this checklist before submitting to the
Innovation Studio.

## Deployed services

- [ ] `ca-arm-migration-planner-api` in `rg-arm-migration-assist` responds
  `200` on `/health`.
- [ ] `GET /api/migration-plans/{runId}` returns the stored plan for at
  least one `runId` per sample repo.
- [ ] `GET /api/migration-plans/{runId}/report.md` and `.html` both return
  `200` with correct MIME and attachment header.
- [ ] Latest merged image tag matches what's deployed. Record in
  `/memories/repo/deployment.md`.

## Sample bundles

For each of `samples/comfyui/` and `samples/open-webui/`, verify:

- [ ] `SOURCE.md`, `PROVENANCE.md`, `assessment.json`, `plan.json`,
  `score.json`, `patches/*.diff`, `migration-result.json`,
  `validation.json`, `report.md`, `report.html`.
- [ ] `assessment.json` validates against the pinned schema.
- [ ] No work item in `plan.json` references a `declared-only` skill.
- [ ] Every patch under `patches/` applies cleanly to the pinned upstream
  SHA (`git apply --check`).
- [ ] `validation.json` outcome matches what the demo claims.

## Frontend

- [ ] React `frontend/` builds (`npm run build`) with no errors.
- [ ] Landing page loads without console errors against the deployed API.
- [ ] Two-repo comparison view renders both bundles.
- [ ] No hard-coded local endpoints. `VITE_API_BASE_URL` set correctly.

## Submission assets

- [ ] `samples/media/screenshots/` populated for both repos and every beat.
- [ ] `samples/media/video/fallback.mp4` present and playable.
- [ ] `samples/media/submission.md` draft complete.
- [ ] Innovation Studio project page updated. Links resolve without login
  from an incognito session.

## RAI + secrets

- [ ] No secrets in the repo (`git grep` for `PRIVATE KEY`, `AZURE_`,
  `password=`).
- [ ] No absolute host user paths in any committed JSON.
- [ ] Reports contain no raw source content, no full prompts.
- [ ] `.env.example` present. `.env` absent.

## Freeze compliance

- [ ] No non-`hotfix/*` merges to `main` after Fri Sep 18 17:00 PT.
- [ ] Schema V1 files unchanged since freeze.

## When any item fails

Do **not** ship. Open a `hotfix/*` branch. Fix. Re-run this checklist.

## When every item passes

Submit before Mon Sep 21 17:00 PT. Record the submission timestamp in
`/memories/repo/submission-calendar.md`.
