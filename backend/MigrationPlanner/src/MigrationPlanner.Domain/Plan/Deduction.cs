using System.Text.Json.Serialization;

namespace MigrationPlanner.Domain.Plan;

public sealed record Deduction(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("magnitude")] int Magnitude,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("evidenceIds")] IReadOnlyList<string> EvidenceIds);
