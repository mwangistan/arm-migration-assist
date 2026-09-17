using MigrationPlanner.Domain.Assessment;
using MigrationPlanner.Domain.Plan;

namespace MigrationPlanner.Application.Abstractions;

/// <summary>
/// Deterministic readiness scorer. The scorer must never consult the AI model
/// and must produce identical output for identical input.
/// </summary>
public interface IReadinessScorer
{
    ReadinessScoreV1 Score(RepositoryAssessmentV1 assessment);
}
