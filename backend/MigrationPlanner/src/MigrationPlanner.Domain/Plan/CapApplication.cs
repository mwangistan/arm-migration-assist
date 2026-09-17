using System.Text.Json.Serialization;

namespace MigrationPlanner.Domain.Plan;

public sealed record CapApplication(
    [property: JsonPropertyName("capId")] CapId CapId,
    [property: JsonPropertyName("ceiling")] int Ceiling,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("triggeredBy")] IReadOnlyList<string> TriggeredBy);
