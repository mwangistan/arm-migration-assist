# Feature 2: AI Migration Planner

Scores readiness, recommends a migration strategy, and produces the migration report.

The API exposes `POST /api/migration-plans` as a same-origin proxy for the
deployed Feature 2 planner. It accepts the complete `RepositoryAssessmentV1`
document produced by `POST /assess` and returns the planner's response without
changing its status code or JSON body. Configure the fixed planner origin with
`MigrationPlanner:BaseUrl`.

| Subfolder | User story |
|-----------|------------|
| `ReadinessScoring/` | 2.1 ARM Readiness Scoring |
| `StrategyGenerator/` | 2.2 Migration Strategy Generator |
| `ReportGeneration/` | 2.3 Migration Report Generation |
