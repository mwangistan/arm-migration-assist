---
mode: 'agent'
description: 'Capture the full demo bundle (screenshots + fallback screencast + submission write-up draft) for both sample repos.'
---

# Task: Capture demo bundle

You are the Demo Experience Lead. Produce the submission-facing artifacts.

## Preconditions

- `samples/comfyui/` and `samples/open-webui/` bundles are complete
  (assessment, plan, score, patches, migration-result, validation, report).
- React `frontend/` is running against the deployed APIs and renders both
  bundles without errors.

## Steps

### Screenshots

1. Hit each demo beat in the React app for both repos. Capture:
   - Landing / URL entry.
   - Findings page with evidence links visible.
   - Score + verdict card.
   - Work items list with an emitted patch highlighted.
   - Report download (Markdown open in a preview).
   - Validation scorecard.
   - Two-repo comparison view.
2. Save under `samples/media/screenshots/<repo>/<beat>.png`.
3. No sensitive absolute paths or tokens visible in any screenshot.

### Fallback screencast

1. Record a single-take walkthrough of the full happy path for one repo
   (ComfyUI recommended — smaller, faster). Target 90–120 seconds.
2. Save under `samples/media/video/fallback.mp4`.
3. This is the **source of truth** if the live demo fails. Rehearse against
   it once before freeze.

### Submission write-up

1. Draft `samples/media/submission.md` covering:
   - Problem in one sentence.
   - What we built (evidence-first, deterministic-before-generative).
   - Reference migrations (ComfyUI + Open WebUI) with measured outcomes.
   - Reusable framework (Assessment → Planner → AutomatedMigration →
     Validation) and the 22-skill catalog.
   - Known limitations and what a real production version would need.
2. Do **not** claim performance or battery improvements without a
   `validation.json` entry supporting them.
3. Do **not** state that a repository is "ready to ship on ARM64" solely
   from static analysis.

### PR

Open a PR titled `chore(samples/media): demo bundle for submission`.

## Rules

- Every screenshot must be from the React frontend, not the wwwroot dev toy.
- Never falsify a screenshot (no post-hoc editing of values, logs, or
  timestamps).
- The fallback screencast wins if there's any doubt.
