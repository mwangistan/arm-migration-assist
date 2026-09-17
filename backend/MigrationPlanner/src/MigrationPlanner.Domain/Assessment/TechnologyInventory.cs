using System.Text.Json.Serialization;

namespace MigrationPlanner.Domain.Assessment;

public sealed record TechnologyInventory(
    [property: JsonPropertyName("languages")] IReadOnlyList<string> Languages,
    [property: JsonPropertyName("frameworks")] IReadOnlyList<string> Frameworks,
    [property: JsonPropertyName("projectTypes")] IReadOnlyList<string> ProjectTypes,
    [property: JsonPropertyName("buildSystems")] IReadOnlyList<string> BuildSystems,
    [property: JsonPropertyName("packageManagers")] IReadOnlyList<string> PackageManagers,
    [property: JsonPropertyName("installers")] IReadOnlyList<string> Installers,
    [property: JsonPropertyName("ciSystems")] IReadOnlyList<string> CiSystems);
