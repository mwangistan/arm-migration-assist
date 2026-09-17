namespace MigrationPlanner.Application.Abstractions;

/// <summary>
/// JSON Schema validator for a raw MigrationPlanV1 payload returned by the
/// planner model. Runs before deserialization so shape drift produces
/// structured errors instead of a JsonException.
/// </summary>
public interface IPlanSchemaValidator
{
    SchemaValidationResult Validate(System.Text.Json.JsonElement payload);
}
