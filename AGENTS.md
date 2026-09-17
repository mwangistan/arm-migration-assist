# ARM Migration Assist — Squad Charter

This file is the source of truth for how the ARM Migration Assist hackathon squad
is organized: who owns what, what artifacts pass between roles, and what "done"
means before submission on **Sept 21, 2026**.

Read this before opening any of the role chatmodes under
[.github/chatmodes/](.github/chatmodes/).

---

## Ground truth (Sept 17, 2026)

- **Deployed:** `ca-arm-migration-planner-api` in resource group
  `rg-arm-migration-assist` — Feature 2 two-pass architecture (deterministic
  `PlanSkeletonBuilder` + `NarrativeFiller` against gpt-4o).
- **Canonical repo:** [`mwangistan/arm-migration-assist`](https://github.com/mwangistan/arm-migration-assist). `origin/main` at `df0ba01` includes
  Features 1, 2 (redesign PR #7), and 4 merged.
- **Contracts (frozen at Fri Sep 18 17:00 PT):** `RepositoryAssessmentV1`,
  `ReadinessScoreV1`, `MigrationPlanV1`, `SkillCatalogV1`,
  `WindowsOnArmGuidanceCorpusV1`. Live schemas under
  [backend/MigrationPlanner/contracts/](backend/MigrationPlanner/contracts/).
- **Skill catalog:** [knowledge/skills/catalog.json](knowledge/skills/catalog.json)
  — 22 declared skills. Only skills marked `runnable` may appear in
  `workItems[]`; others route to `alternatives[]` or `proposedMissingSkills[]`.
- **`.agents/skills/codeblend-ai-composite/`** is an unrelated CodeBlend
  evaluation tool. Do **not** conflate it with the planner skill catalog.

## Branching rules

- No direct commits to `main`. Every role works on a feature branch and opens a
  PR.
- Branch prefix by role: `feature/<role-short>-<slug>` or
  `chore/<slug>`. Examples: `feature/planner-report-generation`,
  `feature/automation-runner-productization`, `chore/catalog-audit`.
- Squad-setup branch: `brianombega/squad-init` (this file lives there first).
- Rebase, do not merge, from `main` when catching up.
- After Fri Sep 18 17:00 PT, only `hotfix/*` branches touch product code.

## Global Definition of Done

Every PR must:

1. Build clean locally (`dotnet build ArmMigrationAssist.slnx`) and in CI.
2. Not modify any V1 schema without Release Captain sign-off.
3. Emit evidence-linked output (no fabricated file paths, dependencies, or
   guidance IDs).
4. Redact secrets from logs, prompts, and stored artifacts.
5. Update its own README if behavior changes.
6. Reference the acceptance criteria it satisfies from
   [docs/project-plan.md](docs/project-plan.md).

---

## Squad roster

Five roles. Explore stays available to all as a shared read-only research
subagent.

### 1. Release Captain / Integrator

**Mission.** Keep `main` shippable end-to-end. Own cross-service wiring, infra,
CI/CD, RAI + redaction, and the submission checklist.

**Owns**

- `AGENTS.md`, `.github/`, `infra/`, root config, `README.md`.
- Container image for `ca-arm-migration-planner-api` and its deployment.
- Sub/RG mapping. Hackathon2026 subscription migration is **deferred** until
  after submission.

**Consumes → Produces.** Green branches from every other role → merged `main`,
deployed API, a working end-to-end demo path, submitted Innovation Studio
package.

**Definition of Done.**

- Every merged PR passes `ci.yml` and does not break `cd.yml`.
- `ca-arm-migration-planner-api` responds `200` on `/health` at freeze.
- Both sample repos run F1→F2→F3→F4 against deployed services and produce
  committed golden bundles under `samples/<repo>/`.
- Innovation Studio submission is complete before Mon Sep 21 17:00 PT.

**Escalation.** Any V1 schema change request. Any credential rotation. Any
production deploy.

---

### 2. Planner Finisher (F2 tail)

**Mission.** Close out User Story 2.3 (Migration Report Generation) inside
`backend/MigrationPlanner/`. Short-lived role — retires after Wave 2.

**Owns**

- `backend/MigrationPlanner/ReportGeneration/` and any related
  `backend/MigrationPlanner/src/MigrationPlanner/Reports/` code.
- `backend/MigrationPlanner/src/MigrationPlanner/Api/` additions for the report
  endpoint only.
- Report fixtures / golden files.

**Consumes → Produces.** Validated `MigrationPlanV1` + `ReadinessScoreV1`
already produced by the planner → deterministic Markdown and HTML rendering
of the report, addressable via `GET /api/migration-plans/{runId}/report.{ext}`.

**Contract rules.**

- Rendering is stateless. No model calls. No new claims. No fabricated URLs.
- Consumes `implementationStatus` on skill catalog entries — never renders a
  work item whose skill is not `runnable`.
- `guidanceId` values are resolved against the pinned corpus version stored on
  the plan. Unknown IDs are dropped, not invented.
- Emits `scoreDigest` in every report footer so a reader can prove the report
  was produced from an untampered score.

**Definition of Done.**

- IMPLEMENTATION_PLAN.md §11 phases 2–7 complete.
- `MarkdownReportRenderer` + `HtmlReportRenderer` with byte-stable golden
  snapshot tests against `blocked-native-app` and both sample fixtures.
- `IPlanArtifactStore` in-memory implementation wired into the planning
  service so runs are retrievable by `runId`.
- `GET /report.{md,html}` returns `Content-Disposition: attachment` with the
  correct MIME type. 404 + Problem Details on unknown `runId`. 415 + Problem
  Details on unsupported extension.
- Zero raw source content, prompts, or secrets in report output.

---

### 3. Automation Engineer (F3 + Skill Catalog)

**Mission.** Turn `backend/AutomatedMigration/` into a demo-quality service and
keep `knowledge/skills/catalog.json` honest. Emit real ARM64 patches for the
two sample repos.

**Owns**

- `backend/AutomatedMigration/` in full.
- `knowledge/skills/catalog.json` and the `SkillCatalogV1` schema shape.
- Any new skill runners.

**First deliverable (blocking).** **Catalog audit** on branch
`chore/catalog-audit`:

1. Add `implementationStatus: "runnable" | "declared-only" | "proposed"` to
   `SkillCatalogV1`.
2. Walk every one of the 22 skills; mark honestly against F1 / F3 / F4 source.
3. For each `runnable` skill add an `implementedBy` pointer to the C# type or
   file that executes it.
4. Bump `catalogVersion`.
5. Land before any work item is emitted by the planner in a demo run.

**Consumes → Produces.** Validated `MigrationPlanV1` from a live planner run →
`patches/*.diff` + `migration-result.json` per sample repo, committed under
`samples/<repo>/`.

**Skill focus for the demo.** Ship these as `runnable` end-to-end:

- `build/add-arm64-target`
- `pipeline/github-actions-arm64-job`
- `pipeline/matrix-fanout` (already partially implemented)
- `code/arch-conditional-cleanup` (via `CodePatcher` + `GitHubModelsChatModel`)

If a skill can't be honestly delivered by Fri Sep 18 12:00, leave it
`declared-only`. No fake progress.

**Definition of Done.**

- `chore/catalog-audit` merged.
- ≥1 valid ARM64 build patch **and** ≥1 CI patch per sample repo.
- `migration-result.json` produced per sample and consumed by F4 successfully.
- Any patch that fails ARM64 build in F4 is downgraded to review-only with
  rationale in the work item — not silently retried.
- Runner invokable by the Demo Experience Lead (thin HTTP wrapper acceptable
  if a full service surface is out of scope).

---

### 4. Demo Experience Lead (F5)

**Mission.** Make the story judgeable through the React `frontend/` and the
submission assets. `frontend/` is the frontend of record — the
`MigrationPlanner/wwwroot/index.html` page stays as a Feature-2 dev toy.

**Owns**

- `frontend/` in full.
- Submission assets: screenshots, 2-minute video, Innovation Studio write-up.
- Demo runbook + recorded fallback screencast.

**Consumes → Produces.** Live deployed APIs and committed sample bundles →
navigable dashboard, findings page, plan + report viewer, PR-package view,
two-repo comparison, screenshots, submission video.

**Feature 5 scope (from [docs/project-plan.md](docs/project-plan.md)).**

- US 4.2 Results Dashboard.
- US 4.3 Reference Repository Demonstrations (both repos, before/after).
- Innovation Studio submission package.

**Definition of Done.**

- Judges can drive both demos end-to-end through the React app against live
  services.
- Recorded fallback screencast committed to `samples/media/` — this is the
  authoritative artifact if the live demo fails.
- Two-repo comparison view renders side-by-side score, blockers, work items,
  validation summary.
- No feature in the UI references a skill that is not `runnable`.

---

### 5. Evidence & Reference-Apps Curator

**Mission.** Populate `samples/comfyui/` and `samples/open-webui/` with
reproducible before/after evidence. Own the golden fixtures that every other
role tests against. Stress-test F1 and F4 by running them on real repos.

**Owns**

- `samples/comfyui/` and `samples/open-webui/`.
- Pinned source SHAs in each `samples/<repo>/SOURCE.md`.
- Golden JSON bundle per repo: `assessment.json`, `plan.json`, `score.json`,
  `patches/*.diff`, `migration-result.json`, `validation.json`,
  `report.md`, `report.html`.

**Consumes → Produces.** Public repos at pinned SHAs → complete evidence
bundles usable as regression fixtures for every downstream role and as the
demo material for F5.

**Rules.**

- Never edit sample repo content in place. Sub-repo material lives under a
  pinned local clone workflow; only produced artifacts are committed.
- Every bundle references the exact deployed image tag / commit that produced
  it (record in `samples/<repo>/PROVENANCE.md`).
- If a bundle regresses on a merge, block the merge — the fixtures are the
  contract.

**Definition of Done.**

- Both sample repos have complete before/after bundles committed under
  `samples/<repo>/`.
- Each bundle reproducible from its `PROVENANCE.md` inside the freeze window.
- F4 Validation scorecards captured for each sample.

---

## Handoff diagram

```text
Repo URL
  │
  ▼
[Curator]                                                       samples/<repo>/SOURCE.md
  │
  ▼
F1 Assessment API ─────► RepositoryAssessmentV1 ──────────────► samples/<repo>/assessment.json
  │
  ▼
F2 MigrationPlanner API ─► ReadinessScoreV1 + MigrationPlanV1 ─► samples/<repo>/{score,plan}.json
  │                       │
  │                       └─► (Planner Finisher) report.md / report.html ─► samples/<repo>/report.*
  │
  ▼
F3 AutomatedMigration ───► patches/*.diff + migration-result.json ► samples/<repo>/patches, migration-result.json
  │
  ▼
F4 Validation API ───────► ValidationScorecard ────────────────► samples/<repo>/validation.json
  │
  ▼
F5 React frontend + submission assets  ───────────────────────► screenshots, video, write-up
```

---

## Wave calendar

- **Wave 0 — Thu Sep 17 AM.** Sync main. `brianombega/squad-init` PR
  (this file + chatmodes + prompts). Automation Engineer opens
  `chore/catalog-audit`. Planner Finisher opens
  `feature/planner-report-generation` and lands US 2.3 phase 2. Curator
  pins sample SHAs.
- **Wave 1 — Thu Sep 17 PM.** Curator posts both assessments to the deployed
  planner; commits plan + score bundles. Automation Engineer runs F3 against
  ComfyUI plan; commits first patch bundle. Demo Experience Lead brings the
  React app up against the live deployed API. Planner Finisher lands US 2.3
  phases 3–5.
- **Wave 2 — Fri Sep 18 AM.** F3 against Open WebUI. Curator runs F4 on
  patched workspaces; commits scorecards. Demo Experience Lead ships the
  two-repo comparison view + PR-package view + screenshots.
- **Wave 3 — Fri Sep 18 PM.** **Freeze at 17:00 PT.** Demo rehearsal +
  fallback screencast recording.
- **Wave 4 — Sat Sep 19 – Mon Sep 21.** `hotfix/*` branches only. Submission
  video, write-up, link verification. Submit before Mon Sep 21 17:00 PT.

## When in doubt

- If a change touches a V1 schema, stop and ping the Release Captain.
- If the planner emits a work item pointing at a `declared-only` skill during
  a demo run, stop and ping the Automation Engineer.
- If a sample bundle regresses, stop the merge and ping the Curator.
- If the deployed API stops responding, stop and ping the Release Captain.
