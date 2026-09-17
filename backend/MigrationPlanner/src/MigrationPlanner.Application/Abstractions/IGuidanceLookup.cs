using MigrationPlanner.Domain.Guidance;

namespace MigrationPlanner.Application.Abstractions;

/// <summary>
/// Narrow view over the Windows on Arm guidance corpus exposed to the planner
/// model. This is the only tool the model is permitted to invoke during
/// plan generation; all other MCP tools are prefetched by the orchestrator.
/// Implementations are per-run and are not thread-safe.
/// </summary>
public interface IGuidanceLookup
{
    string CorpusVersion { get; }

    /// <summary>
    /// Retrieval calls the implementation still permits before it will throw
    /// <see cref="GuidanceLookupBudgetExceededException"/>. Enumeration via
    /// <see cref="ListIndex"/> does not consume budget.
    /// </summary>
    int RemainingBudget { get; }

    IReadOnlyCollection<string> RetrievedGuidanceIds { get; }

    IReadOnlyList<GuidanceIndexEntry> ListIndex();

    Task<GuidanceSnippet?> LookupByIdAsync(string guidanceId, CancellationToken cancellationToken);

    Task<IReadOnlyList<GuidanceSnippet>> LookupByTopicAsync(Topic topic, CancellationToken cancellationToken);
}
