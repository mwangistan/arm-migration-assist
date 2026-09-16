using MigrationPlanner.Domain.Assessment;
using MigrationPlanner.Domain.Plan;

namespace MigrationPlanner.Application.Abstractions;

public sealed record PlanSafetyResult(bool IsSafe, string? ErrorCode, IReadOnlyList<string> Violations)
{
    public static PlanSafetyResult Ok { get; } =
        new(true, null, Array.Empty<string>());

    public static PlanSafetyResult Fail(string errorCode, params string[] violations) =>
        new(false, errorCode, violations);
}

/// <summary>
/// Post-model plan validator. Verifies the plan is evidence-linked, references
/// only known skills/guidance, and contains no repository-mutating instructions.
/// </summary>
public interface IPlanSafetyValidator
{
    PlanSafetyResult Validate(
        MigrationPlanV1 plan,
        RepositoryAssessmentV1 assessment,
        ReadinessScoreV1 score);
}
