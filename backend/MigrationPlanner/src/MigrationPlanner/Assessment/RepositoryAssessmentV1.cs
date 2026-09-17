using System.Text.Json.Serialization;

namespace MigrationPlanner.Assessment;

public sealed record RepositoryAssessmentV1(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("assessmentId")] string AssessmentId,
    [property: JsonPropertyName("generatedAt")] DateTimeOffset GeneratedAt,
    [property: JsonPropertyName("producer")] Producer Producer,
    [property: JsonPropertyName("repository")] Repository Repository,
    [property: JsonPropertyName("technology")] TechnologyInventory Technology,
    [property: JsonPropertyName("dependencies")] IReadOnlyList<DependencyFinding> Dependencies,
    [property: JsonPropertyName("codeFindings")] IReadOnlyList<CodeFinding> CodeFindings,
    [property: JsonPropertyName("buildFindings")] BuildFindings BuildFindings,
    [property: JsonPropertyName("windowsExperience")] WindowsExperience WindowsExperience,
    [property: JsonPropertyName("scanCoverage")] ScanCoverage ScanCoverage,
    [property: JsonPropertyName("unknowns")] IReadOnlyList<Unknown> Unknowns,
    [property: JsonPropertyName("availableSkills")] IReadOnlyList<Skill> AvailableSkills);
