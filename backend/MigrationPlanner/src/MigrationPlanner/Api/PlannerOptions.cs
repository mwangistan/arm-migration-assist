namespace MigrationPlanner.Api;

public sealed class PlannerOptions
{
    public const string CorsPolicyName = "planner-cors";

    public string CorpusVersion { get; set; } = "2026-09-15.1";

    public string[] AllowedOrigins { get; set; } = Array.Empty<string>();

    public string ProvenanceProvider { get; set; } = "foundry";
    public string ProvenanceName { get; set; } = "gpt-4o";
    public string ProvenanceVersion { get; set; } = "2024.11.20";

    /// <summary>Skip the LLM narrative pass and ship the deterministic skeleton as-is.</summary>
    public bool SkeletonOnly { get; set; }
}
