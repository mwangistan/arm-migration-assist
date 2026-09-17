using MigrationPlanner.Domain.Assessment;

namespace MigrationPlanner.Application.Abstractions;

public sealed record EvidenceValidationResult(bool IsValid, string? ErrorCode, IReadOnlyList<string> Errors)
{
    public static EvidenceValidationResult Ok { get; } =
        new(true, null, Array.Empty<string>());

    public static EvidenceValidationResult Fail(string errorCode, params string[] errors) =>
        new(false, errorCode, errors);
}

/// <summary>
/// Enforces cross-record rules that JSON Schema cannot express plus the
/// maxLength/maxItems guards required by the input schema. Produces stable
/// error codes so callers can convert results into Problem Details.
/// </summary>
public interface IEvidenceValidator
{
    EvidenceValidationResult Validate(RepositoryAssessmentV1 assessment);
}
