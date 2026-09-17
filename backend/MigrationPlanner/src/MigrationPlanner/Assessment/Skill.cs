using System.Text.Json.Serialization;

namespace MigrationPlanner.Assessment;

public sealed record Skill(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("writeAccess")] bool WriteAccess,
    [property: JsonPropertyName("supportedInputs")] IReadOnlyList<string> SupportedInputs,
    [property: JsonPropertyName("supportedOutputs")] IReadOnlyList<string> SupportedOutputs);
