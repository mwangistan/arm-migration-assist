using ArmMigrationAssist.RepositoryDiscovery.Models;

namespace ArmMigrationAssist.RepositoryDiscovery.Validation;

public static class RepositoryAssessmentValidator
{
    public static IReadOnlyList<string> Validate(RepositoryAssessment assessment)
    {
        ArgumentNullException.ThrowIfNull(assessment);

        var errors = new List<string>();
        var evidenceIds = assessment.Dependencies
            .Select(finding => finding.EvidenceId)
            .Concat(assessment.CodeFindings.Select(finding => finding.EvidenceId))
            .Append(assessment.BuildFindings.EvidenceId)
            .ToArray();

        foreach (var duplicate in evidenceIds
                     .GroupBy(id => id, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1)
                     .Select(group => group.Key)
                     .Order(StringComparer.Ordinal))
        {
            errors.Add($"Duplicate evidenceId '{duplicate}'.");
        }

        if (assessment.ScanCoverage.FilesScanned > assessment.ScanCoverage.FilesTotal)
        {
            errors.Add(
                $"filesScanned ({assessment.ScanCoverage.FilesScanned}) exceeds filesTotal ({assessment.ScanCoverage.FilesTotal}).");
        }

        var knownEvidenceIds = evidenceIds.ToHashSet(StringComparer.Ordinal);
        foreach (var unresolvedId in assessment.Unknowns
                     .SelectMany(unknown => unknown.EvidenceIds)
                     .Where(id => !knownEvidenceIds.Contains(id))
                     .Distinct(StringComparer.Ordinal)
                     .Order(StringComparer.Ordinal))
        {
            errors.Add($"Unknown references unresolved evidenceId '{unresolvedId}'.");
        }

        return errors;
    }

    public static void EnsureValid(RepositoryAssessment assessment)
    {
        var errors = Validate(assessment);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                $"The repository assessment violates cross-record invariants: {string.Join(" ", errors)}");
        }
    }
}