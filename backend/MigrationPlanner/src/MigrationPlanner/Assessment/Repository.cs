using System.Text.Json.Serialization;

namespace MigrationPlanner.Assessment;

public sealed record Repository(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("commitSha")] string CommitSha,
    [property: JsonPropertyName("defaultBranch")] string DefaultBranch,
    [property: JsonPropertyName("license")] string? License = null);
