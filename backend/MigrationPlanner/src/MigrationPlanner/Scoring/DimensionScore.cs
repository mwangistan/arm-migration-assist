using System.Text.Json.Serialization;

namespace MigrationPlanner.Scoring;

public sealed record DimensionScore(
    [property: JsonPropertyName("dimensionKey")] DimensionKey DimensionKey,
    [property: JsonPropertyName("weightPct")] int WeightPct,
    [property: JsonPropertyName("rawScore")] int RawScore,
    [property: JsonPropertyName("weightedContribution")] double WeightedContribution,
    [property: JsonPropertyName("deductions")] IReadOnlyList<Deduction> Deductions,
    [property: JsonPropertyName("confidence")] double Confidence,
    [property: JsonPropertyName("rationaleCodes")] IReadOnlyList<string> RationaleCodes,
    [property: JsonPropertyName("evidenceIds")] IReadOnlyList<string> EvidenceIds);
