namespace MigrationPlanner.Application.Abstractions;

/// <summary>Plan JSON plus optional user-facing observations (e.g. tool-call counts).</summary>
public sealed record PlannerModelResult(string PlanJson, IReadOnlyList<string> Observations)
{
    public static PlannerModelResult FromJson(string json) => new(json, Array.Empty<string>());
}
