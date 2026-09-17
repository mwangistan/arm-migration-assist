using System.Text.Json.Serialization;

namespace MigrationPlanner.Assessment;

public sealed record DependencyFinding(
    [property: JsonPropertyName("evidenceId")] string EvidenceId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("ecosystem")] string Ecosystem,
    [property: JsonPropertyName("type")] DependencyType Type,
    [property: JsonPropertyName("criticality")] Criticality Criticality,
    [property: JsonPropertyName("architectureStatus")] ArchitectureStatus ArchitectureStatus,
    [property: JsonPropertyName("availableArchitectures")] IReadOnlyList<Architecture> AvailableArchitectures,
    [property: JsonPropertyName("replacementCandidates")] IReadOnlyList<string> ReplacementCandidates,
    [property: JsonPropertyName("evidence")] IReadOnlyList<Evidence> Evidence,
    [property: JsonPropertyName("confidence")] double Confidence,
    [property: JsonPropertyName("version")] string? Version = null);
