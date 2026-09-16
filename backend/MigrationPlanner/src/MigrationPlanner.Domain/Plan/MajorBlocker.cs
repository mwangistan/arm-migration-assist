using System.Text.Json.Serialization;

namespace MigrationPlanner.Domain.Plan;

public sealed record MajorBlocker(
    [property: JsonPropertyName("blockerId")] string BlockerId,
    [property: JsonPropertyName("category")] BlockerCategory Category,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("evidenceIds")] IReadOnlyList<string> EvidenceIds);
