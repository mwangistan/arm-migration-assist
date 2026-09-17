using System.Text.Json.Serialization;

namespace MigrationPlanner.Domain.Assessment;

/// <summary>
/// One concrete piece of evidence. Must reference exactly one of <see cref="Path"/> or
/// <see cref="Artifact"/>; the evidence validator enforces the JSON Schema oneOf.
/// </summary>
public sealed record Evidence(
    [property: JsonPropertyName("sourceType")] SourceType SourceType,
    [property: JsonPropertyName("observation")] string Observation,
    [property: JsonPropertyName("path"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Path = null,
    [property: JsonPropertyName("artifact"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Artifact = null);
