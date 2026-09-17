using MigrationPlanner.Application.Abstractions;
using MigrationPlanner.Domain.Assessment;
using MigrationPlanner.Domain.Errors;

namespace MigrationPlanner.Infrastructure.Validation;

/// <summary>
/// Enforces cross-record rules that JSON Schema cannot express plus the
/// maxLength / maxItems guards required by the input contract. Every violation
/// returns a stable error code so violations produce Problem Details, not 500.
/// </summary>
public sealed class EvidenceValidator : IEvidenceValidator
{
    private const int MaxAssessmentId = 64;
    private const int MaxDependencies = 10_000;
    private const int MaxCodeFindings = 20_000;
    private const int MaxUnknowns = 1_000;
    private const int MaxSkills = 500;
    private const int MaxEvidencePerFinding = 40;

    public EvidenceValidationResult Validate(RepositoryAssessmentV1 assessment)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        var errors = new List<string>();

        if (string.IsNullOrEmpty(assessment.AssessmentId) || assessment.AssessmentId.Length > MaxAssessmentId)
        {
            errors.Add($"assessmentId length must be 1..{MaxAssessmentId}.");
        }

        if (assessment.Dependencies.Count > MaxDependencies)
        {
            errors.Add($"dependencies exceeds maxItems ({MaxDependencies}).");
        }

        if (assessment.CodeFindings.Count > MaxCodeFindings)
        {
            errors.Add($"codeFindings exceeds maxItems ({MaxCodeFindings}).");
        }

        if (assessment.Unknowns.Count > MaxUnknowns)
        {
            errors.Add($"unknowns exceeds maxItems ({MaxUnknowns}).");
        }

        if (assessment.AvailableSkills.Count > MaxSkills)
        {
            errors.Add($"availableSkills exceeds maxItems ({MaxSkills}).");
        }

        if (assessment.ScanCoverage.FilesScanned > assessment.ScanCoverage.FilesTotal)
        {
            errors.Add("scanCoverage.filesScanned must be <= scanCoverage.filesTotal.");
            return EvidenceValidationResult.Fail(PlannerErrorCode.ScannerCoverageInvalid, errors.ToArray());
        }

        var seenIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var dep in assessment.Dependencies)
        {
            RegisterEvidenceId(seenIds, dep.EvidenceId, "dependencies", errors);
            ValidateEvidenceList(dep.Evidence, $"dependencies[{dep.EvidenceId}].evidence", errors);
        }

        foreach (var code in assessment.CodeFindings)
        {
            RegisterEvidenceId(seenIds, code.EvidenceId, "codeFindings", errors);
            ValidateEvidenceList(code.Evidence, $"codeFindings[{code.EvidenceId}].evidence", errors);
        }

        RegisterEvidenceId(seenIds, assessment.BuildFindings.EvidenceId, "buildFindings", errors);
        ValidateEvidenceList(assessment.BuildFindings.Evidence, "buildFindings.evidence", errors);
        ValidateEvidenceList(assessment.WindowsExperience.Evidence, "windowsExperience.evidence", errors);

        if (errors.Count == 0)
        {
            return EvidenceValidationResult.Ok;
        }

        var code2 = errors.Any(e => e.StartsWith("duplicate evidenceId", StringComparison.Ordinal))
            ? PlannerErrorCode.EvidenceDuplicateId
            : PlannerErrorCode.EvidenceInvalid;

        return EvidenceValidationResult.Fail(code2, errors.ToArray());
    }

    private static void RegisterEvidenceId(HashSet<string> seen, string evidenceId, string owner, List<string> errors)
    {
        if (string.IsNullOrEmpty(evidenceId))
        {
            errors.Add($"{owner}: evidenceId is required.");
            return;
        }

        if (!seen.Add(evidenceId))
        {
            errors.Add($"duplicate evidenceId '{evidenceId}' referenced by {owner}.");
        }
    }

    private static void ValidateEvidenceList(IReadOnlyList<Evidence> list, string owner, List<string> errors)
    {
        if (list.Count == 0)
        {
            errors.Add($"{owner}: at least one evidence entry is required.");
        }

        if (list.Count > MaxEvidencePerFinding)
        {
            errors.Add($"{owner}: exceeds maxItems ({MaxEvidencePerFinding}).");
        }

        foreach (var evidence in list)
        {
            var hasPath = !string.IsNullOrEmpty(evidence.Path);
            var hasArtifact = !string.IsNullOrEmpty(evidence.Artifact);
            if (hasPath == hasArtifact)
            {
                errors.Add($"{owner}: exactly one of 'path' or 'artifact' must be set.");
            }
        }
    }
}
