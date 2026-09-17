using System.Text.Json;
using Json.Schema;
using MigrationPlanner.Application.Abstractions;
using MigrationPlanner.Domain.Errors;

namespace MigrationPlanner.Infrastructure.Schema;

/// <summary>
/// JSON Schema validator for MigrationPlanV1. Runs on the raw model output
/// before deserialization; on failure the orchestrator emits
/// <c>planner.plan.shapeInvalid</c> with the schema-error list so a repair
/// loop or caller can act on structured errors.
/// </summary>
public sealed class PlanSchemaValidator : IPlanSchemaValidator
{
    private const int MaxErrorsReported = 12;

    private readonly JsonSchema _schema;

    public PlanSchemaValidator()
    {
        var schemaText = EmbeddedSchemaResources.ReadText(
            EmbeddedSchemaResources.MigrationPlanV1ResourceName);
        _schema = JsonSchema.FromText(schemaText);
    }

    public SchemaValidationResult Validate(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object)
        {
            return SchemaValidationResult.Fail(PlannerErrorCode.PlanShapeInvalid,
                "Plan payload must be a JSON object.");
        }

        var options = new EvaluationOptions
        {
            OutputFormat = OutputFormat.List,
            RequireFormatValidation = true,
        };

        var result = _schema.Evaluate(payload, options);
        if (result.IsValid)
        {
            return SchemaValidationResult.Ok;
        }

        var errors = Flatten(result).Take(MaxErrorsReported).ToArray();
        return SchemaValidationResult.Fail(PlannerErrorCode.PlanShapeInvalid, errors);
    }

    private static IEnumerable<string> Flatten(EvaluationResults results)
    {
        if (results.HasErrors && results.Errors is not null)
        {
            foreach (var error in results.Errors)
            {
                var location = results.InstanceLocation.ToString();
                yield return string.IsNullOrEmpty(location)
                    ? $"{error.Key}: {error.Value}"
                    : $"{location} {error.Key}: {error.Value}";
            }
        }

        foreach (var detail in results.Details)
        {
            foreach (var nested in Flatten(detail))
            {
                yield return nested;
            }
        }
    }
}
