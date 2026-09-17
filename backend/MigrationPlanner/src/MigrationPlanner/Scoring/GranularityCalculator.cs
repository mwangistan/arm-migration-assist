using MigrationPlanner.Assessment;
using MigrationPlanner.Scoring;

namespace MigrationPlanner.Scoring;

/// <summary>
/// Computes the deterministic minimum workItems bucket coverage a plan must
/// exhibit for a given assessment + score. Used both to enrich the model
/// prompt (rule 20) and to enforce granularity server-side after the fact.
/// </summary>
public static class GranularityCalculator
{
    public sealed record ExpectedBucket(
        string Category,
        string Description,
        IReadOnlyList<string> EvidenceIds);

    public sealed record Expectation(
        IReadOnlyList<ExpectedBucket> Buckets,
        int MinimumWorkItems)
    {
        public bool IsMet(int actualWorkItems) => actualWorkItems >= MinimumWorkItems;
    }

    public static Expectation Compute(RepositoryAssessmentV1 assessment, ReadinessScoreV1 score)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        ArgumentNullException.ThrowIfNull(score);

        var buckets = new List<ExpectedBucket>();

        foreach (var dep in assessment.Dependencies
                     .Where(d => d.Criticality == Criticality.Required
                                 && (d.ArchitectureStatus == ArchitectureStatus.Blocked
                                     || d.ArchitectureStatus == ArchitectureStatus.EmulationOnly))
                     .OrderBy(d => d.EvidenceId, StringComparer.Ordinal))
        {
            buckets.Add(new ExpectedBucket(
                Category: "dep",
                Description: $"Port required dependency '{dep.Name}' ({FriendlyStatus(dep.ArchitectureStatus)}) to ARM64.",
                EvidenceIds: new[] { dep.EvidenceId }));
        }

        var build = assessment.BuildFindings;
        var buildEvidence = new[] { build.EvidenceId };
        if (!build.Arm64TargetExists && !build.Arm64EcTargetExists && !IsInterpretedOnly(assessment))
        {
            buckets.Add(new ExpectedBucket(
                "build",
                "Add an ARM64 or Arm64EC build target.",
                buildEvidence));
        }
        if (!build.Arm64CiJobExists)
        {
            buckets.Add(new ExpectedBucket(
                "build",
                "Add an ARM64 CI job.",
                buildEvidence));
        }
        if (!build.PackagingSupportsArm64 && HasPackagingSurface(assessment))
        {
            buckets.Add(new ExpectedBucket(
                "build",
                "Add ARM64 packaging support.",
                buildEvidence));
        }
        if (!build.TestsExist)
        {
            buckets.Add(new ExpectedBucket(
                "build",
                "Introduce a test suite that runs on ARM64.",
                buildEvidence));
        }

        foreach (var finding in assessment.CodeFindings
                     .Where(f => f.Severity == Severity.Critical)
                     .OrderBy(f => f.EvidenceId, StringComparer.Ordinal))
        {
            buckets.Add(new ExpectedBucket(
                "code",
                $"Fix critical code finding '{finding.RuleId}' in {finding.File}.",
                new[] { finding.EvidenceId }));
        }

        return new Expectation(buckets, buckets.Count);
    }

    private static bool IsInterpretedOnly(RepositoryAssessmentV1 assessment)
    {
        var interpreted = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "python", "javascript", "typescript", "ruby", "php", "perl", "lua",
        };
        var langs = assessment.Technology.Languages;
        return langs.Count > 0 && langs.All(l => interpreted.Contains(l));
    }

    private static bool HasPackagingSurface(RepositoryAssessmentV1 assessment) =>
        assessment.Technology.Installers.Count > 0
        || assessment.Technology.PackageManagers.Any(p =>
            p.Equals("msix", StringComparison.OrdinalIgnoreCase)
            || p.Equals("wix", StringComparison.OrdinalIgnoreCase)
            || p.Equals("nuget", StringComparison.OrdinalIgnoreCase));

    private static string FriendlyStatus(ArchitectureStatus status) => status switch
    {
        ArchitectureStatus.Blocked => "blocked",
        ArchitectureStatus.EmulationOnly => "emulation-only",
        ArchitectureStatus.Unknown => "unknown",
        _ => status.ToString().ToLowerInvariant(),
    };
}
