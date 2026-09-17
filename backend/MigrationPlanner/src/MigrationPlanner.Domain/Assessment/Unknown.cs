using System.Text.Json.Serialization;

namespace MigrationPlanner.Domain.Assessment;

public sealed record Unknown(
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("area")] UnknownArea Area,
    [property: JsonPropertyName("requiredSkill")] string? RequiredSkill = null,
    [property: JsonPropertyName("evidenceIds")] IReadOnlyList<string>? EvidenceIds = null);
