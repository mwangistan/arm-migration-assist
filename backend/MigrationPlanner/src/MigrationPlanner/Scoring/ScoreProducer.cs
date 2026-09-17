using System.Text.Json.Serialization;

namespace MigrationPlanner.Scoring;

public sealed record ScoreProducer(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("ruleset")] string Ruleset);
