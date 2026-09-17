using System.Text.Json.Serialization;

namespace MigrationPlanner.Assessment;

public sealed record BuildFindings(
    [property: JsonPropertyName("evidenceId")] string EvidenceId,
    [property: JsonPropertyName("arm64TargetExists")] bool Arm64TargetExists,
    [property: JsonPropertyName("arm64EcTargetExists")] bool Arm64EcTargetExists,
    [property: JsonPropertyName("arm64CiJobExists")] bool Arm64CiJobExists,
    [property: JsonPropertyName("packagingSupportsArm64")] bool PackagingSupportsArm64,
    [property: JsonPropertyName("testsExist")] bool TestsExist,
    [property: JsonPropertyName("detectedTargets")] IReadOnlyList<string> DetectedTargets,
    [property: JsonPropertyName("evidence")] IReadOnlyList<Evidence> Evidence);
