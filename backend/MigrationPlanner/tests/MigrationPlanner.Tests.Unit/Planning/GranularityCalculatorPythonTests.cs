using FluentAssertions;
using MigrationPlanner.Application.Planning;
using MigrationPlanner.Domain.Assessment;
using MigrationPlanner.Domain.Plan;
using Xunit;

namespace MigrationPlanner.Tests.Unit.Planning;

// Guards the Python-project routing buckets: on a Python repo with pip deps,
// the granularity contract must direct the model at the python/* audit skills
// instead of the MSBuild-oriented default buckets.
public sealed class GranularityCalculatorPythonTests
{
    [Fact]
    public void Python_repo_with_pip_deps_adds_native_wheel_and_scaffold_buckets()
    {
        var assessment = BuildAssessment(
            languages: ["python"],
            dependencies:
            [
                Dep("dep-numpy-01", "numpy"),
                Dep("dep-pillow-01", "Pillow"),
            ]);
        var buckets = GranularityCalculator.Compute(assessment, BuildScore()).Buckets;

        buckets.Should().Contain(b => b.Description.Contains("python/native-wheel-audit"));
        buckets.Should().Contain(b => b.Description.Contains("python/pip-constraints-arm64-scaffold"));
        buckets.Should().NotContain(b => b.Description.Contains("python/pytorch-arm64-wheel-audit"));
        buckets.Should().NotContain(b => b.Description.Contains("python/cuda-to-directml-audit"));
    }

    [Fact]
    public void Python_repo_with_torch_adds_pytorch_and_cuda_buckets()
    {
        var assessment = BuildAssessment(
            languages: ["python"],
            dependencies:
            [
                Dep("dep-torch-01", "torch"),
                Dep("dep-torchvision-01", "torchvision"),
            ]);
        var buckets = GranularityCalculator.Compute(assessment, BuildScore()).Buckets;

        buckets.Should().Contain(b => b.Description.Contains("python/native-wheel-audit"));
        buckets.Should().Contain(b => b.Description.Contains("python/pytorch-arm64-wheel-audit"));
        buckets.Should().Contain(b => b.Description.Contains("python/cuda-to-directml-audit"));
        buckets.Should().Contain(b => b.Description.Contains("python/pip-constraints-arm64-scaffold"));
    }

    [Fact]
    public void Non_python_repo_gets_no_python_buckets()
    {
        var assessment = BuildAssessment(
            languages: ["csharp"],
            dependencies:
            [
                Dep("dep-json-01", "Newtonsoft.Json", ecosystem: "nuget"),
            ]);
        var buckets = GranularityCalculator.Compute(assessment, BuildScore()).Buckets;

        buckets.Should().NotContain(b => b.Description.Contains("python/"));
    }

    [Fact]
    public void Python_repo_without_pip_deps_gets_no_python_buckets()
    {
        var assessment = BuildAssessment(languages: ["python"], dependencies: []);
        var buckets = GranularityCalculator.Compute(assessment, BuildScore()).Buckets;

        buckets.Should().NotContain(b => b.Description.Contains("python/"));
    }

    [Fact]
    public void Python_torch_bucket_cites_torch_evidence_id()
    {
        var assessment = BuildAssessment(
            languages: ["python"],
            dependencies:
            [
                Dep("dep-numpy-01", "numpy"),
                Dep("dep-torch-01", "torch"),
            ]);
        var pytorchBucket = GranularityCalculator.Compute(assessment, BuildScore()).Buckets
            .First(b => b.Description.Contains("python/pytorch-arm64-wheel-audit"));

        pytorchBucket.EvidenceIds.Should().Contain("dep-torch-01");
        pytorchBucket.EvidenceIds.Should().NotContain("dep-numpy-01");
    }

    private static DependencyFinding Dep(string evidenceId, string name, string ecosystem = "pypi") => new(
        EvidenceId: evidenceId,
        Name: name,
        Ecosystem: ecosystem,
        Type: DependencyType.Native,
        Criticality: Criticality.Required,
        ArchitectureStatus: ArchitectureStatus.Unknown,
        AvailableArchitectures: Array.Empty<Architecture>(),
        ReplacementCandidates: Array.Empty<string>(),
        Evidence: Array.Empty<Evidence>(),
        Confidence: 0.9);

    private static RepositoryAssessmentV1 BuildAssessment(
        IReadOnlyList<string> languages,
        IReadOnlyList<DependencyFinding> dependencies) => new(
        SchemaVersion: "1.0",
        AssessmentId: "assessment-python-test",
        GeneratedAt: DateTimeOffset.UtcNow,
        Producer: new Producer("test", "0.1.0", "test"),
        Repository: new Repository("test", "https://github.com/example/test", new string('0', 40), "main"),
        Technology: new TechnologyInventory(
            Languages: languages,
            Frameworks: Array.Empty<string>(),
            ProjectTypes: Array.Empty<string>(),
            BuildSystems: Array.Empty<string>(),
            PackageManagers: Array.Empty<string>(),
            Installers: Array.Empty<string>(),
            CiSystems: Array.Empty<string>()),
        Dependencies: dependencies,
        CodeFindings: Array.Empty<CodeFinding>(),
        BuildFindings: new BuildFindings(
            EvidenceId: "build-01",
            Arm64TargetExists: false,
            Arm64EcTargetExists: false,
            Arm64CiJobExists: false,
            PackagingSupportsArm64: false,
            TestsExist: false,
            DetectedTargets: Array.Empty<string>(),
            Evidence: Array.Empty<Evidence>()),
        WindowsExperience: new WindowsExperience(
            WindowsVersionExists: false,
            UiTechnology: UiTechnology.Web,
            InstallerExists: false,
            OfflineCapable: false,
            AccessibilityEvidence: AccessibilityEvidenceLevel.Unknown,
            NotificationsIntegrated: false,
            LifecycleIntegrated: false,
            Evidence: Array.Empty<Evidence>()),
        ScanCoverage: new ScanCoverage(1, 1, 1.0, ["scanner"], Array.Empty<string>()),
        Unknowns: Array.Empty<Unknown>(),
        AvailableSkills: Array.Empty<Skill>());

    private static ReadinessScoreV1 BuildScore() => new()
    {
        SchemaVersion = "1.0",
        AssessmentId = "assessment-python-test",
        GeneratedAt = DateTimeOffset.UtcNow,
        OverallScore = 50,
        UncappedScore = 50,
        Band = ReadinessBand.ModerateMigration,
        EvidenceCompleteness = ConfidenceLabel.High,
        EvidenceCompletenessScore = 0.9,
        Provisional = false,
        ScoreSummary = "test summary",
    };
}
