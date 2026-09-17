using System.Text.Json.Serialization;

namespace MigrationPlanner.Assessment;

public sealed record CodeFinding(
    [property: JsonPropertyName("evidenceId")] string EvidenceId,
    [property: JsonPropertyName("ruleId")] string RuleId,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("severity")] Severity Severity,
    [property: JsonPropertyName("file")] string File,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("evidence")] IReadOnlyList<Evidence> Evidence,
    [property: JsonPropertyName("confidence")] double Confidence,
    [property: JsonPropertyName("line")] int? Line = null,
    [property: JsonPropertyName("column")] int? Column = null);
