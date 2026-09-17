---
mode: 'agent'
description: 'Run the Feature 1 assessment against a public GitHub repository and commit the golden bundle under samples/<slug>/.'
---

# Task: Assess a repository

You are the Evidence Curator. You will produce a reproducible F1 assessment
bundle for `${input:repoUrl:Public GitHub URL (e.g. https://github.com/comfyanonymous/ComfyUI)}`
at commit `${input:sha:Pinned commit SHA}` and store it under
`samples/${input:slug:Sample folder slug (e.g. comfyui)}/`.

## Steps

1. Confirm the sample folder exists under `samples/${input:slug}/`. If not,
   create it.
2. Write `samples/${input:slug}/SOURCE.md` with:
   - Upstream URL.
   - Pinned commit SHA.
   - License note.
   - Exact `git clone` command a reviewer would run.
3. Ensure the Feature 1 API is running locally (see repo README) or use the
   deployed instance recorded in `/memories/repo/deployment.md`.
4. Submit the repository to `POST /assess` with target `ARM64 Native` unless
   overridden.
5. Save the raw response body to `samples/${input:slug}/assessment.json`.
6. Validate the file against
   `backend/Assessment/RepositoryAssessmentV1.schema.json`. Do not commit
   invalid JSON.
7. Update `samples/${input:slug}/PROVENANCE.md` with:
   - Deployed image tag or local commit SHA.
   - API endpoint used.
   - Timestamp.
   - This repo's `git rev-parse HEAD`.
8. Redact any tokens or absolute file paths that leak host user names from
   the captured JSON before committing.
9. Open a PR titled `chore(samples/${input:slug}): golden assessment
   bundle`.

## Rules

- Never hand-edit `assessment.json` after capture. If the response is wrong,
  file a bug against Feature 1 — do not paper over it.
- Never commit the cloned sample repo source. Only produced artifacts.
- Never commit tokens, SAS URLs, or connection strings.
