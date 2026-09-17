using System.Text.Json.Serialization;

namespace MigrationPlanner.Domain.Plan;

// TODO: bind to MigrationPlanV1 schema when authored.
public sealed record MigrationPlanV1
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; init; } = "1.0";

    [JsonPropertyName("assessmentId")]
    public string AssessmentId { get; init; } = string.Empty;

    [JsonExtensionData]
    public IDictionary<string, System.Text.Json.JsonElement>? AdditionalProperties { get; init; }
}
