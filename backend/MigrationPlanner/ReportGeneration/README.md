# 2.3 Migration Report Generation

Turns the validated `MigrationPlanV1` + `ReadinessScoreV1` pair produced by the
planner into a portable, human-readable report an engineering lead can drop
into a PR description, a Teams post, or an email.

Reports are **pure renderings** of an already-produced plan artifact:

- They must not call the model.
- They must not mutate the plan.
- They must not add new claims or fabricate URLs.
- If a field is empty in the plan, it stays empty in the report.

## Formats

| Format   | Status  | Content-Type                  | Extension | Notes                                             |
|----------|---------|-------------------------------|-----------|---------------------------------------------------|
| Markdown | MVP     | `text/markdown; charset=utf-8`| `md`      | Copy-pastes cleanly into GitHub / ADO / Teams.    |
| HTML     | MVP     | `text/html; charset=utf-8`    | `html`    | Single self-contained file; printable via browser.|
| PDF      | Later   | `application/pdf`             | `pdf`     | QuestPDF, same view model.                        |
| Bundle   | Later   | `application/zip`             | `zip`     | `report.md` + `plan.json` + `score.json` + `assessment.json`. |

## Contracts (Application layer)

Contracts live in
[`src/MigrationPlanner.Application/Reporting/`](../src/MigrationPlanner.Application/Reporting/):

- `MigrationReport` — view model wrapping `RunId`, `AssessmentId`,
  `RepositoryIdentity`, plan, score, and warnings.
- `RepositoryIdentity` — small snapshot lifted from `RepositoryAssessmentV1.repository`.
- `ReportFormat` — enum keying renderers.
- `IMigrationReportRenderer` — one implementation per format.
- `MigrationReportFactory` — deterministic projection from `(assessment, PlanResult)`
  to `MigrationReport`. Rejects failed `PlanResult`s.

## Report layout

Sections are ordered evidence → verdict → action, not by JSON field order:

1. Header — repo name, commit SHA, generatedAt, model provenance,
   corpus version, `runId`, `scoreDigest`.
2. Verdict — `recommendedPath`, `confidence`, banded overall score,
   applied caps, provisional flag.
3. Executive summary (verbatim from `plan.executiveSummary`).
4. Score interpretation + a table of the five weighted dimensions.
5. Facts (`plan.facts[]`) with evidence and `guidanceId` footnotes.
6. Inferences (`plan.inferences[]`), labeled "planner-inferred".
7. Recommended work items grouped by priority.
8. Alternatives considered (`plan.alternatives[]`).
9. Risks + unknowns (combined table).
10. Validation plan (`plan.validationPlan[]`).
11. Missing skills + required approvals (gating items).
12. Appendix — full plan and score JSON.

## HTTP surface (not yet wired)

- `GET /api/migration-plans/{runId}/report.{ext}` — `ext ∈ { md, html }`.
- 404 + Problem Details for unknown `runId`.
- 415 + Problem Details for unsupported `ext`.
- `Content-Disposition: attachment; filename=migration-report-{runId}.{ext}`
  for browser downloads.

## Guardrails

- Rendering is stateless and side-effect-free apart from reading the
  artifact store.
- `guidanceId` citations resolve against the same corpus version recorded on
  the plan; unknown IDs are dropped, not fabricated. `sourceUrl` values are
  copied verbatim from the corpus snapshot.
- `scoreDigest` is rendered in the footer of every format so a reader can
  prove the report was produced from an untampered score.
- Renderers never emit secrets, raw source content, or full prompts.

## Phased delivery

1. Contracts, `MigrationReport` view model, `MigrationReportFactory`,
   unit tests. **(done)**
2. `MarkdownReportRenderer` + golden-file snapshot tests against the three
   demo fixtures.
3. `IPlanArtifactStore` + in-memory implementation, wired into the existing
   planning service so runs are persisted on success.
4. `HtmlReportRenderer` + golden-file snapshot tests.
5. Report download endpoint + integration tests.
6. Demo UI download buttons.
