namespace MigrationPlanner.Infrastructure.Model;

public sealed class PhiModelOptions
{
    public const string SectionName = "Planner:Phi";

    /// <summary>
    /// Azure AI Foundry inference endpoint, e.g.
    /// <c>https://foundry-arm-mig-assist.services.ai.azure.com/</c>.
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>
    /// Foundry deployment name for the Phi model (not the model catalog name).
    /// </summary>
    public string DeploymentName { get; set; } = "phi-4";

    /// <summary>
    /// Optional static API key. Leave empty to use <c>DefaultAzureCredential</c>
    /// (managed identity in Azure, `az login` locally).
    /// </summary>
    public string? ApiKey { get; set; }

    public int MaxOutputTokens { get; set; } = 4096;

    public float Temperature { get; set; }

    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(90);

    public string ProvenanceProvider { get; set; } = "foundry";

    public string ProvenanceName { get; set; } = "phi-4";

    public string ProvenanceVersion { get; set; } = "7.0.0";
}
