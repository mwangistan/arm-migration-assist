namespace MigrationPlanner.Application.Abstractions;

public sealed record SchemaValidationResult(bool IsValid, string? ErrorCode, IReadOnlyList<string> Errors)
{
    public static SchemaValidationResult Ok { get; } =
        new(true, null, Array.Empty<string>());

    public static SchemaValidationResult Fail(string errorCode, params string[] errors) =>
        new(false, errorCode, errors);
}

/// <summary>
/// Validates a raw JSON payload against the RepositoryAssessmentV1 JSON Schema
/// at the API boundary. Rejects unsupported major versions.
/// </summary>
public interface IAssessmentSchemaValidator
{
    SchemaValidationResult Validate(System.Text.Json.JsonElement payload);
}
