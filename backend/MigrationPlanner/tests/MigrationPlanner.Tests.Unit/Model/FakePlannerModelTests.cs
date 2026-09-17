using System.Text.Json;
using FluentAssertions;
using MigrationPlanner.Application.Abstractions;
using MigrationPlanner.Domain.Assessment;
using MigrationPlanner.Domain.Guidance;
using MigrationPlanner.Domain.Plan;
using MigrationPlanner.Infrastructure.Model;
using MigrationPlanner.Infrastructure.Scoring;
using MigrationPlanner.Tests.Unit.Scoring;
using Xunit;

namespace MigrationPlanner.Tests.Unit.Model;

public sealed class FakePlannerModelTests
{
    private static readonly DeterministicReadinessScorer Scorer = new();
    private static readonly FakePlannerModel Model = new();

    [Fact]
    public async Task ReadyBand_NoProvisional_RecommendsNativeArm64High()
    {
        var (path, conf) = await MapAsync(ScoringAssessmentBuilder.Ready());
        path.Should().Be("native-arm64");
        conf.Should().Be("high");
    }

    [Fact]
    public async Task BandInsufficientEvidence_ForcesInsufficientEvidenceLow()
    {
        var coverage = new ScanCoverage(
            FilesScanned: 5,
            FilesTotal: 100,
            DependencyResolutionRate: 1.0,
            ScannersCompleted: new[] { "dep" },
            ScannersFailed: Array.Empty<string>());

        var (path, conf) = await MapAsync(ScoringAssessmentBuilder.Ready(coverage: coverage));

        path.Should().Be("insufficient-evidence");
        conf.Should().Be("low");
    }

    [Fact]
    public async Task ProvisionalWithNumericBand_KeepsBandPathAndDowngradesConfidenceToLow()
    {
        var unknowns = new[]
        {
            new Unknown("Windows UI unknown", UnknownArea.WindowsExperience,
                EvidenceIds: Array.Empty<string>()),
        };

        var (path, conf, score) = await MapWithScoreAsync(
            ScoringAssessmentBuilder.Ready(unknowns: unknowns));

        score.Provisional.Should().BeTrue();
        score.Band.Should().NotBe(ReadinessBand.InsufficientEvidence,
            because: "the single windowsExperience unknown should not force insufficient-evidence coverage");
        path.Should().NotBe("insufficient-evidence",
            because: "provisional now only downgrades confidence, not the path");
        conf.Should().Be("low");
    }

    [Fact]
    public async Task NoArm64TargetCap_WithProvisional_MapsToNativeArm64Low()
    {
        var build = new BuildFindings(
            EvidenceId: "build-001",
            Arm64TargetExists: false,
            Arm64EcTargetExists: false,
            Arm64CiJobExists: false,
            PackagingSupportsArm64: false,
            TestsExist: true,
            DetectedTargets: Array.Empty<string>(),
            Evidence: new[] { new Evidence(SourceType.Manifest, "no arm64", Path: "src/App.csproj") });

        var unknowns = new[]
        {
            new Unknown("Windows UI unknown", UnknownArea.WindowsExperience,
                EvidenceIds: Array.Empty<string>()),
        };

        var (path, conf, score) = await MapWithScoreAsync(
            ScoringAssessmentBuilder.Ready(build: build, unknowns: unknowns));

        score.Provisional.Should().BeTrue();
        score.CapsApplied.Should().Contain(c => c.CapId == CapId.NoArm64OrArm64EcTargetLe60);
        path.Should().Be("native-arm64");
        conf.Should().Be("low");
    }

    [Fact]
    public async Task MissingArm64Target_EmitsFeatureThreeBuildAndPipelineWork()
    {
        var build = new BuildFindings(
            EvidenceId: "build-001",
            Arm64TargetExists: false,
            Arm64EcTargetExists: false,
            Arm64CiJobExists: false,
            PackagingSupportsArm64: true,
            TestsExist: true,
            DetectedTargets: new[] { "x64" },
            Evidence: new[] { new Evidence(SourceType.Manifest, "x64 only", Path: "src/App.csproj") });
        var assessment = ScoringAssessmentBuilder.Ready(build: build);
        var score = Scorer.Score(assessment);

        var modelResult = await Model.GeneratePlanJsonAsync(
            assessment,
            score,
            new NoopGuidance(),
            CancellationToken.None);
        using var document = JsonDocument.Parse(modelResult.PlanJson);
        var workItems = document.RootElement.GetProperty("workItems").EnumerateArray().ToArray();
        var buildItem = workItems.Single(item =>
            item.GetProperty("agentOrSkill").GetString() == "build-config-generator");
        var pipelineItem = workItems.Single(item =>
            item.GetProperty("agentOrSkill").GetString() == "ci-pipeline-generator");

        buildItem.GetProperty("inputs")[0].GetString().Should().Be("src/App.csproj");
        pipelineItem.GetProperty("dependencies")[0].GetString()
            .Should().Be(buildItem.GetProperty("id").GetString());
    }

    private static async Task<(string Path, string Confidence)> MapAsync(RepositoryAssessmentV1 assessment)
    {
        var (path, conf, _) = await MapWithScoreAsync(assessment);
        return (path, conf);
    }

    private static async Task<(string Path, string Confidence, ReadinessScoreV1 Score)> MapWithScoreAsync(
        RepositoryAssessmentV1 assessment)
    {
        var score = Scorer.Score(assessment);
        var modelResult = await Model.GeneratePlanJsonAsync(assessment, score, new NoopGuidance(), CancellationToken.None);
        using var doc = JsonDocument.Parse(modelResult.PlanJson);
        return (
            doc.RootElement.GetProperty("recommendedPath").GetString()!,
            doc.RootElement.GetProperty("confidence").GetString()!,
            score);
    }

    private sealed class NoopGuidance : IGuidanceLookup
    {
        public string CorpusVersion => "2026-09-15.1";
        public int RemainingBudget => 0;
        public IReadOnlyCollection<string> RetrievedGuidanceIds => Array.Empty<string>();
        public IReadOnlyList<GuidanceIndexEntry> ListIndex() => Array.Empty<GuidanceIndexEntry>();
        public Task<GuidanceSnippet?> LookupByIdAsync(string guidanceId, CancellationToken cancellationToken) =>
            Task.FromResult<GuidanceSnippet?>(null);
        public Task<IReadOnlyList<GuidanceSnippet>> LookupByTopicAsync(Topic topic, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<GuidanceSnippet>>(Array.Empty<GuidanceSnippet>());
    }
}
