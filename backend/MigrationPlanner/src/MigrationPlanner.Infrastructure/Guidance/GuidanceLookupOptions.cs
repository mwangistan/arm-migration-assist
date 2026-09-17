namespace MigrationPlanner.Infrastructure.Guidance;

public sealed class GuidanceLookupOptions
{
    public const string SectionName = "Planner:GuidanceLookup";

    /// <summary>
    /// Maximum number of retrieval calls (by-id or by-topic) the planner model
    /// may make during a single run. Enumerating the index does not count.
    /// </summary>
    public int MaxCalls { get; set; } = 8;
}
