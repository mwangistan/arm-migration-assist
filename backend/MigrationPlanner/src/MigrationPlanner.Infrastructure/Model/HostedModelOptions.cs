namespace MigrationPlanner.Infrastructure.Model;

public sealed class HostedModelOptions
{
    public const string SectionName = "Planner:Hosted";

    /// <summary>
    /// Azure AI Foundry inference endpoint, e.g.
    /// <c>https://foundry-arm-mig-assist.services.ai.azure.com/</c>.
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>
    /// Foundry deployment name for the hosted (GPT-4 class) model. Not the
    /// model catalog name.
    /// </summary>
    public string DeploymentName { get; set; } = "gpt-4o";

    /// <summary>
    /// Optional static API key. Leave empty to use <c>DefaultAzureCredential</c>
    /// (managed identity in Azure, `az login` locally).
    /// </summary>
    public string? ApiKey { get; set; }

    // gpt-4o class models emit the full MigrationPlanV1 JSON in one completion.
    // 4096 truncates verbose plans (finishReason=length) and yields invalid JSON,
    // so cap at the deployment's 16384-token ceiling to avoid truncation.
    public int MaxOutputTokens { get; set; } = 16384;

    public float Temperature { get; set; }

    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(90);

    public string ProvenanceProvider { get; set; } = "foundry";

    public string ProvenanceName { get; set; } = "gpt-4o";

    public string ProvenanceVersion { get; set; } = "2024.11.20";

    /// <summary>Expose the read-only guidance-lookup MCP tool as a function tool.</summary>
    public bool EnableGuidanceLookupTool { get; set; } = true;

    /// <summary>Maximum tool-invocation rounds before forcing a final answer.</summary>
    public int MaxToolRounds { get; set; } = 4;
}
