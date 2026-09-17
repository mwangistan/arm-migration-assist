using System.Text.Json.Serialization;

namespace MigrationPlanner.Assessment;

public sealed record ScannerVersion(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("version")] string Version);

public sealed record Producer(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("ruleset")] string? Ruleset = null,
    [property: JsonPropertyName("scannerVersions")] IReadOnlyList<ScannerVersion>? ScannerVersions = null);
