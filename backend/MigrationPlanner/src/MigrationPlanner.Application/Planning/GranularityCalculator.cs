using MigrationPlanner.Domain.Assessment;
using MigrationPlanner.Domain.Plan;

namespace MigrationPlanner.Application.Planning;

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

        AppendPythonBuckets(assessment, buckets);

        return new Expectation(buckets, buckets.Count);
    }

    // Python-AI-inference projects don't have a MSBuild build target to add. Their
    // per-package wheel matrix + CUDA→DirectML routing is the real migration work.
    // Each bucket names the concrete python/* skill so the model routes into the
    // right runner instead of collapsing everything into the generic build bucket.
    private static void AppendPythonBuckets(RepositoryAssessmentV1 assessment, List<ExpectedBucket> buckets)
    {
        if (!IsPythonRepository(assessment))
            return;

        var pipDeps = assessment.Dependencies
            .Where(d => string.Equals(d.Ecosystem, "pypi", StringComparison.OrdinalIgnoreCase))
            .OrderBy(d => d.EvidenceId, StringComparer.Ordinal)
            .ToList();

        if (pipDeps.Count == 0)
            return;

        var allPipEvidence = pipDeps
            .Select(d => d.EvidenceId)
            .Distinct(StringComparer.Ordinal)
            .Take(10)
            .ToArray();

        buckets.Add(new ExpectedBucket(
            "python-dep",
            "Audit pip native-wheel availability for ARM64 via python/native-wheel-audit.",
            allPipEvidence));

        var torchDeps = pipDeps.Where(d => IsTorchPackage(d.Name)).ToList();
        var torchEvidence = torchDeps
            .Select(d => d.EvidenceId)
            .Distinct(StringComparer.Ordinal)
            .Take(4)
            .ToArray();

        if (torchEvidence.Length > 0)
        {
            buckets.Add(new ExpectedBucket(
                "python-dep",
                "Audit PyTorch ARM64 wheel status via python/pytorch-arm64-wheel-audit.",
                torchEvidence));
            buckets.Add(new ExpectedBucket(
                "python-code",
                "Enumerate CUDA usage sites for DirectML / ONNX Runtime routing via python/cuda-to-directml-audit.",
                torchEvidence));
        }

        buckets.Add(new ExpectedBucket(
            "python-dep",
            "Scaffold constraints-arm64.txt with blank pins via python/pip-constraints-arm64-scaffold.",
            new[] { allPipEvidence[0] }));
    }

    private static bool IsPythonRepository(RepositoryAssessmentV1 assessment) =>
        assessment.Technology.Languages
            .Any(l => string.Equals(l, "python", StringComparison.OrdinalIgnoreCase));

    private static bool IsTorchPackage(string name)
    {
        var normalized = (name ?? string.Empty).ToLowerInvariant().Replace('_', '-').Replace('.', '-');
        return normalized == "torch"
            || normalized == "torchvision"
            || normalized == "torchaudio"
            || normalized == "torch-directml";
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
