using System.Text.Json.Serialization;

namespace MigrationPlanner.Assessment;

public sealed record ScanCoverage(
    [property: JsonPropertyName("filesScanned")] long FilesScanned,
    [property: JsonPropertyName("filesTotal")] long FilesTotal,
    [property: JsonPropertyName("dependencyResolutionRate")] double DependencyResolutionRate,
    [property: JsonPropertyName("scannersCompleted")] IReadOnlyList<string> ScannersCompleted,
    [property: JsonPropertyName("scannersFailed")] IReadOnlyList<string> ScannersFailed);
