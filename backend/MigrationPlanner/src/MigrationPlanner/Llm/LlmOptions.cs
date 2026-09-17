namespace MigrationPlanner.Llm;

public sealed class LlmOptions
{
    public const string SectionName = "Planner:Llm";

    public string Endpoint { get; set; } = string.Empty;
    public string DeploymentName { get; set; } = "gpt-4o";
    public string? ApiKey { get; set; }
    public int MaxOutputTokens { get; set; } = 4096;
    public float Temperature { get; set; }
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(60);
    public int MaxNarrativeChars { get; set; } = 2000;
}
