using MigrationPlanner.Domain.Plan;

namespace MigrationPlanner.Application.Abstractions;

/// <summary>
/// In-memory cache for validated planner results, keyed by scoreDigest.
/// Skips the model call entirely when the same assessment is re-submitted.
/// Backing store is intentionally per-process; a new deployment revision
/// starts with an empty cache.
/// </summary>
public interface IPlanCache
{
    bool TryGet(string scoreDigest, out CachedPlan? cached);

    void Store(string scoreDigest, MigrationPlanV1 plan, ReadinessScoreV1 score, IReadOnlyList<string> observations);
}

public sealed record CachedPlan(
    MigrationPlanV1 Plan,
    ReadinessScoreV1 Score,
    DateTimeOffset StoredAt,
    IReadOnlyList<string> Observations);
