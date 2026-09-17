# Feature 2: AI Migration Planner

Scores readiness, recommends a migration strategy, and produces the migration report.

| Subfolder | User story |
|-----------|------------|
| `ReadinessScoring/` | 2.1 ARM Readiness Scoring |
| `StrategyGenerator/` | 2.2 Migration Strategy Generator |
| `ReportGeneration/` | 2.3 Migration Report Generation |

## Feature 1 input

Feature 2 consumes `RepositoryAssessmentV1` without scraping the dashboard or
reading scanner internals. Supported integration options are:

- inject and call `IRepositoryAssessmentService` in the same .NET process
- load the contract from `GET /api/contracts/repository-assessment/v1`
- submit `POST /api/assessment-jobs`, poll its `statusUrl`, then read `resultUrl`
- subscribe to `eventsUrl` with SSE and read `resultUrl` after a `completed` event
- watch the CLI's atomically replaced JSON output file when process isolation is
	preferred

SSE is used instead of Windows Notification Facility (WNF) so HTTP consumers,
containers, Linux hosts, and Windows processes share one protocol. Events are
notifications, not the source of truth; planning begins only after the result
endpoint returns schema-valid JSON and
`RepositoryAssessmentValidator.EnsureValid` accepts its cross-record evidence.

Queued job state is memory-backed, bounded to 100 retained records, and kept for
up to one hour. A production planner should persist the completed assessment
before scoring it and use
`assessmentId`, repository commit SHA, producer version, and ruleset for
idempotency and audit correlation.
