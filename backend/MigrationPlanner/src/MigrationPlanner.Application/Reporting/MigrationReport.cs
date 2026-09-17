using MigrationPlanner.Domain.Plan;

namespace MigrationPlanner.Application.Reporting;

public sealed record MigrationReport(
    string RunId,
    string AssessmentId,
    RepositoryIdentity Repository,
    MigrationPlanV1 Plan,
    ReadinessScoreV1 Score,
    IReadOnlyList<string> Warnings);
