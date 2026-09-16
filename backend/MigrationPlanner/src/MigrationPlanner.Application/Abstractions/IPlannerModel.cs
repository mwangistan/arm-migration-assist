using MigrationPlanner.Domain.Assessment;
using MigrationPlanner.Domain.Plan;

namespace MigrationPlanner.Application.Abstractions;

/// <summary>
/// Boundary for the AI reasoning model. Implementations must be replaceable
/// without changing planner contracts. Never modifies input; returns raw JSON
/// so the orchestrator can enforce schema/safety validation on the model output.
/// </summary>
public interface IPlannerModel
{
    /// <summary>
    /// Produces a `MigrationPlanV1`-shaped JSON string for the given assessment
    /// and deterministic score. Deterministic implementations must return a
    /// stable string for the same input. <paramref name="guidanceLookup"/> is
    /// the only tool the model is permitted to invoke during generation; every
    /// other MCP tool has already been prefetched by the orchestrator and is
    /// available in the planner input the caller assembled.
    /// <paramref name="retryHint"/> is non-null when the orchestrator is
    /// re-invoking the model after a rejected first attempt; implementations
    /// should surface it in the prompt so the model can produce a corrected
    /// plan.
    /// </summary>
    Task<string> GeneratePlanJsonAsync(
        RepositoryAssessmentV1 assessment,
        ReadinessScoreV1 score,
        IGuidanceLookup guidanceLookup,
        CancellationToken cancellationToken,
        PlannerRetryHint? retryHint = null);
}
