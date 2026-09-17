using System.Text.Json;
using Json.Schema;
using MigrationPlanner.Application.Abstractions;
using MigrationPlanner.Domain.Errors;

namespace MigrationPlanner.Infrastructure.Schema;

/// <summary>
/// JSON Schema boundary validator for RepositoryAssessmentV1. Rejects unsupported
/// major versions before any DTO deserialization occurs. Enables strict format
/// assertions so `date-time` and `uri` are enforced.
/// </summary>
public sealed class AssessmentSchemaValidator : IAssessmentSchemaValidator
{
    private readonly JsonSchema _schema;

    public AssessmentSchemaValidator()
    {
        var schemaText = EmbeddedSchemaResources.ReadText(
            EmbeddedSchemaResources.RepositoryAssessmentV1ResourceName);
        _schema = JsonSchema.FromText(schemaText);
    }

    public SchemaValidationResult Validate(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object)
        {
            return SchemaValidationResult.Fail(PlannerErrorCode.SchemaInvalid,
                "Payload must be a JSON object.");
        }

        if (payload.TryGetProperty("schemaVersion", out var version) &&
            version.ValueKind == JsonValueKind.String)
        {
            var text = version.GetString();
            if (!string.IsNullOrEmpty(text) && !text.StartsWith("1.", StringComparison.Ordinal))
            {
                return SchemaValidationResult.Fail(PlannerErrorCode.SchemaUnsupportedVersion,
                    $"Unsupported schemaVersion '{text}'. Only 1.x is accepted.");
            }
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

        var errors = Flatten(result).ToArray();
        return SchemaValidationResult.Fail(PlannerErrorCode.SchemaInvalid, errors);
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
