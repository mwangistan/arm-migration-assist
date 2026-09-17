using MigrationPlanner.Domain.Errors;
using MigrationPlanner.Domain.Plan;

namespace MigrationPlanner.Application.Planning;

public sealed record PlanResult(
    bool IsSuccess,
    MigrationPlanV1? Plan,
    ReadinessScoreV1? Score,
    string RunId,
    IReadOnlyList<string> Warnings,
    string? ErrorCode = null,
    IReadOnlyList<string>? Errors = null,
    TimeSpan? RetryAfter = null)
{
    public static PlanResult Ok(MigrationPlanV1 plan, ReadinessScoreV1 score, string runId, IReadOnlyList<string> warnings) =>
        new(true, plan, score, runId, warnings);

    public static PlanResult Fail(string errorCode, string runId, params string[] errors) =>
        new(false, null, null, runId, Array.Empty<string>(), errorCode, errors);

    public static PlanResult RateLimited(string runId, TimeSpan? retryAfter, string message) =>
        new(false, null, null, runId, Array.Empty<string>(),
            PlannerErrorCode.ModelRateLimited, new[] { message }, retryAfter);
}
