using System.Text.Json.Serialization;

namespace MigrationPlanner.Domain.Plan;

/// <summary>
/// Deterministic Windows-on-Arm readiness score. Serialization order matches
/// the required-property order in <c>contracts/ReadinessScoreV1.schema.json</c>
/// so canonical JSON (RFC 8785) hashing is stable for <c>scoreDigest</c>.
/// </summary>
public sealed record ReadinessScoreV1
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; init; } = "1.0";

    [JsonPropertyName("assessmentId")]
    public string AssessmentId { get; init; } = string.Empty;

    [JsonPropertyName("generatedAt")]
    public DateTimeOffset GeneratedAt { get; init; }

    [JsonPropertyName("producer")]
    public ScoreProducer Producer { get; init; } =
        new(Name: "arm-migration-assist-scorer", Version: "0.1.0", Ruleset: "scoring-v1");

    [JsonPropertyName("overallScore")]
    public int OverallScore { get; init; }

    [JsonPropertyName("uncappedScore")]
    public int UncappedScore { get; init; }

    [JsonPropertyName("band")]
    public ReadinessBand Band { get; init; } = ReadinessBand.InsufficientEvidence;

    [JsonPropertyName("confidence")]
    public ConfidenceLabel Confidence { get; init; } = ConfidenceLabel.Low;

    [JsonPropertyName("confidenceScore")]
    public double ConfidenceScore { get; init; }

    [JsonPropertyName("provisional")]
    public bool Provisional { get; init; } = true;

    [JsonPropertyName("provisionalReasons")]
    public IReadOnlyList<ProvisionalReason> ProvisionalReasons { get; init; } =
        Array.Empty<ProvisionalReason>();

    [JsonPropertyName("dimensions")]
    public IReadOnlyList<DimensionScore> Dimensions { get; init; } = Array.Empty<DimensionScore>();

    [JsonPropertyName("capsApplied")]
    public IReadOnlyList<CapApplication> CapsApplied { get; init; } = Array.Empty<CapApplication>();

    [JsonPropertyName("majorBlockers")]
    public IReadOnlyList<MajorBlocker> MajorBlockers { get; init; } = Array.Empty<MajorBlocker>();

    [JsonPropertyName("rationaleCodes")]
    public IReadOnlyList<string> RationaleCodes { get; init; } = Array.Empty<string>();

    [JsonPropertyName("evidenceIds")]
    public IReadOnlyList<string> EvidenceIds { get; init; } = Array.Empty<string>();
}
