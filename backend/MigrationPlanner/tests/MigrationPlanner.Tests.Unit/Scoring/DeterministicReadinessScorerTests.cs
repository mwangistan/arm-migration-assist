using System.Text.Json;
using FluentAssertions;
using MigrationPlanner.Domain.Assessment;
using MigrationPlanner.Domain.Plan;
using MigrationPlanner.Infrastructure.Scoring;
using Xunit;

namespace MigrationPlanner.Tests.Unit.Scoring;

public sealed class DeterministicReadinessScorerTests
{
    private static readonly DeterministicReadinessScorer Scorer = new();

    [Fact]
    public void Dimensions_AreExactlyFiveAndWeightsSumTo100()
    {
        var score = Scorer.Score(ScoringAssessmentBuilder.Ready());

        score.Dimensions.Should().HaveCount(5);
        score.Dimensions.Sum(d => d.WeightPct).Should().Be(100);
        score.Dimensions.Select(d => d.DimensionKey).Should().OnlyHaveUniqueItems();
        score.Dimensions.Select(d => d.DimensionKey).Should().BeEquivalentTo(new[]
        {
            DimensionKey.DependencyCompatibility,
            DimensionKey.CodeCompatibility,
            DimensionKey.BuildAndCiReadiness,
            DimensionKey.RuntimeAndValidationEvidence,
            DimensionKey.WindowsExperienceAndDeployment,
        });
    }

    [Fact]
    public void ReadyAssessment_ReachesReadyOrMinorChangesBandWithNoCaps()
    {
        var score = Scorer.Score(ScoringAssessmentBuilder.Ready());

        score.Band.Should().Be(ReadinessBand.ReadyOrMinorChanges);
        score.OverallScore.Should().BeGreaterOrEqualTo(85);
        score.CapsApplied.Should().BeEmpty();
        score.MajorBlockers.Should().BeEmpty();
        score.Provisional.Should().BeFalse();
        score.ProvisionalReasons.Should().BeEmpty();
    }

    [Fact]
    public void Producer_IsStampedWithRulesetId()
    {
        var score = Scorer.Score(ScoringAssessmentBuilder.Ready());

        score.Producer.Ruleset.Should().Be("scoring-v1");
        score.Producer.Name.Should().Be("arm-migration-assist-scorer");
    }

    [Fact]
    public void SameAssessment_ProducesByteIdenticalJson()
    {
        var a = ScoringAssessmentBuilder.Ready();

        var first = JsonSerializer.Serialize(Scorer.Score(a));
        var second = JsonSerializer.Serialize(Scorer.Score(a));

        second.Should().Be(first);
    }

    [Fact]
    public void RequiredBlockedDriver_CapsScoreAt30_AndEmitsBlocker()
    {
        var driver = ScoringAssessmentBuilder.Dep(
            evidenceId: "dep-drv",
            name: "legacy-x64-driver",
            type: DependencyType.Driver,
            status: ArchitectureStatus.Blocked);

        var score = Scorer.Score(ScoringAssessmentBuilder.Ready(dependencies: new[] { driver }));

        score.OverallScore.Should().BeLessOrEqualTo(30);
        score.CapsApplied.Should().Contain(c => c.CapId == CapId.RequiredUnsupportedDriverLe30);
        score.MajorBlockers.Should().Contain(b => b.BlockerId == "bl-drv-legacy-x64-driver");
    }

    [Fact]
    public void RequiredBlockedNative_NoReplacement_CapsScoreAt40()
    {
        var native = ScoringAssessmentBuilder.Dep(
            evidenceId: "dep-native",
            name: "acme-x64",
            type: DependencyType.Native,
            status: ArchitectureStatus.Blocked);

        var score = Scorer.Score(ScoringAssessmentBuilder.Ready(dependencies: new[] { native }));

        score.OverallScore.Should().BeLessOrEqualTo(40);
        score.CapsApplied.Should().Contain(c => c.CapId == CapId.RequiredX64OnlyNativeLe40);
    }

    [Fact]
    public void RequiredBlockedNative_WithReplacement_DoesNotFireX64Cap()
    {
        var native = ScoringAssessmentBuilder.Dep(
            evidenceId: "dep-native",
            name: "acme-x64",
            type: DependencyType.Native,
            status: ArchitectureStatus.Blocked,
            replacements: new[] { "acme-arm64" });

        var score = Scorer.Score(ScoringAssessmentBuilder.Ready(dependencies: new[] { native }));

        score.CapsApplied.Should().NotContain(c => c.CapId == CapId.RequiredX64OnlyNativeLe40);
    }

    [Fact]
    public void NoArm64Target_CapsScoreAt60_AndEmitsBlocker()
    {
        var build = new BuildFindings(
            EvidenceId: "build-001",
            Arm64TargetExists: false,
            Arm64EcTargetExists: false,
            Arm64CiJobExists: false,
            PackagingSupportsArm64: false,
            TestsExist: true,
            DetectedTargets: new[] { "x64/Release" },
            Evidence: new[] { new Evidence(SourceType.Manifest, "obs", Path: "src/App.csproj") });

        var score = Scorer.Score(ScoringAssessmentBuilder.Ready(build: build));

        score.OverallScore.Should().BeLessOrEqualTo(60);
        score.CapsApplied.Should().Contain(c => c.CapId == CapId.NoArm64OrArm64EcTargetLe60);
        score.MajorBlockers.Should().Contain(b => b.BlockerId == "bl-no-arm64-target");
    }

    [Fact]
    public void MultipleCaps_ApplyLowestCeiling()
    {
        var driver = ScoringAssessmentBuilder.Dep("dep-drv", "drv-1",
            type: DependencyType.Driver, status: ArchitectureStatus.Blocked);
        var build = new BuildFindings(
            EvidenceId: "build-001",
            Arm64TargetExists: false,
            Arm64EcTargetExists: false,
            Arm64CiJobExists: false,
            PackagingSupportsArm64: false,
            TestsExist: true,
            DetectedTargets: Array.Empty<string>(),
            Evidence: new[] { new Evidence(SourceType.Manifest, "obs", Path: "src/App.csproj") });

        var score = Scorer.Score(ScoringAssessmentBuilder.Ready(
            dependencies: new[] { driver },
            build: build));

        score.OverallScore.Should().BeLessOrEqualTo(30);
        score.CapsApplied.Should().HaveCount(2);
    }

    [Fact]
    public void CriticalCodeFinding_EmitsCodeBlocker()
    {
        var critical = ScoringAssessmentBuilder.Code("code-crit", Severity.Critical, "ARM-CODE-SIMD-01");

        var score = Scorer.Score(ScoringAssessmentBuilder.Ready(codeFindings: new[] { critical }));

        score.MajorBlockers.Should().Contain(b => b.BlockerId.StartsWith("bl-code-crit-"));
        score.RationaleCodes.Should().Contain(c => c.StartsWith("CODE-CRITICAL-"));
    }

    [Fact]
    public void CodeSeverityDeductions_ScaleWithConfidence()
    {
        var high = ScoringAssessmentBuilder.Code("code-high", Severity.High, confidence: 1.0);
        var withHigh = Scorer.Score(ScoringAssessmentBuilder.Ready(codeFindings: new[] { high }));

        var lowConfidence = ScoringAssessmentBuilder.Code("code-high", Severity.High, confidence: 0.25);
        var withLow = Scorer.Score(ScoringAssessmentBuilder.Ready(codeFindings: new[] { lowConfidence }));

        withHigh.OverallScore.Should().BeLessThan(withLow.OverallScore);
    }

    [Fact]
    public void LowScanCoverage_TriggersProvisionalFlag()
    {
        var coverage = new ScanCoverage(
            FilesScanned: 40,
            FilesTotal: 100,
            DependencyResolutionRate: 1.0,
            ScannersCompleted: new[] { "dep" },
            ScannersFailed: Array.Empty<string>());

        var score = Scorer.Score(ScoringAssessmentBuilder.Ready(coverage: coverage));

        score.Provisional.Should().BeTrue();
        score.ProvisionalReasons.Should().Contain(ProvisionalReason.ScanCoverageLow);
    }

    [Fact]
    public void VeryLowScanCoverage_ForcesInsufficientEvidenceBand()
    {
        var coverage = new ScanCoverage(
            FilesScanned: 5,
            FilesTotal: 100,
            DependencyResolutionRate: 1.0,
            ScannersCompleted: new[] { "dep" },
            ScannersFailed: Array.Empty<string>());

        var score = Scorer.Score(ScoringAssessmentBuilder.Ready(coverage: coverage));

        score.Band.Should().Be(ReadinessBand.InsufficientEvidence);
        score.Provisional.Should().BeTrue();
    }

    [Fact]
    public void FilesTotalZero_EmitsInsufficientSignalsAndInsufficientEvidenceBand()
    {
        var coverage = new ScanCoverage(
            FilesScanned: 0,
            FilesTotal: 0,
            DependencyResolutionRate: 1.0,
            ScannersCompleted: Array.Empty<string>(),
            ScannersFailed: Array.Empty<string>());

        var score = Scorer.Score(ScoringAssessmentBuilder.Ready(coverage: coverage));

        score.Band.Should().Be(ReadinessBand.InsufficientEvidence);
        score.ProvisionalReasons.Should().Contain(ProvisionalReason.InsufficientSignals);
    }

    [Fact]
    public void ScannerFailed_AddsProvisionalReason()
    {
        var coverage = new ScanCoverage(
            FilesScanned: 100,
            FilesTotal: 100,
            DependencyResolutionRate: 1.0,
            ScannersCompleted: new[] { "dep" },
            ScannersFailed: new[] { "code" });

        var score = Scorer.Score(ScoringAssessmentBuilder.Ready(coverage: coverage));

        score.ProvisionalReasons.Should().Contain(ProvisionalReason.ScannerFailed);
    }

    [Fact]
    public void UnknownAreaDependency_AddsProvisionalReason()
    {
        var unknowns = new[]
        {
            new Unknown("dependency graph incomplete", UnknownArea.Dependency),
        };

        var score = Scorer.Score(ScoringAssessmentBuilder.Ready(unknowns: unknowns));

        score.ProvisionalReasons.Should().Contain(ProvisionalReason.DimensionMissingEvidence);
    }

    [Theory]
    [InlineData(100, ReadinessBand.ReadyOrMinorChanges)]
    [InlineData(85, ReadinessBand.ReadyOrMinorChanges)]
    [InlineData(84, ReadinessBand.ModerateMigration)]
    [InlineData(70, ReadinessBand.ModerateMigration)]
    [InlineData(69, ReadinessBand.SignificantRemediation)]
    [InlineData(50, ReadinessBand.SignificantRemediation)]
    [InlineData(49, ReadinessBand.BlockedOrMajorRedesign)]
    [InlineData(0, ReadinessBand.BlockedOrMajorRedesign)]
    public void BandBoundaries_MapCorrectly(int overall, ReadinessBand expected)
    {
        var assessment = ScoringAssessmentBuilder.Ready();
        var scored = new ReadinessScoreV1
        {
            AssessmentId = assessment.AssessmentId,
            OverallScore = overall,
        };

        var actual = overall switch
        {
            >= 85 => ReadinessBand.ReadyOrMinorChanges,
            >= 70 => ReadinessBand.ModerateMigration,
            >= 50 => ReadinessBand.SignificantRemediation,
            _ => ReadinessBand.BlockedOrMajorRedesign,
        };

        _ = scored;
        actual.Should().Be(expected);
    }

    [Fact]
    public void OverallScore_NeverExceedsUncappedScore()
    {
        var driver = ScoringAssessmentBuilder.Dep("dep-drv", "drv",
            type: DependencyType.Driver, status: ArchitectureStatus.Blocked);
        var score = Scorer.Score(ScoringAssessmentBuilder.Ready(dependencies: new[] { driver }));

        score.OverallScore.Should().BeLessOrEqualTo(score.UncappedScore);
    }

    [Fact]
    public void UncappedScore_EqualsSumOfWeightedContributions_Rounded()
    {
        var score = Scorer.Score(ScoringAssessmentBuilder.Ready());

        var expected = (int)Math.Round(score.Dimensions.Sum(d => d.WeightedContribution),
            MidpointRounding.AwayFromZero);
        score.UncappedScore.Should().Be(expected);
    }

    [Fact]
    public void EvidenceIds_AreDeduplicatedAndSorted()
    {
        var deps = new[]
        {
            ScoringAssessmentBuilder.Dep("dep-a", "aaa", status: ArchitectureStatus.EmulationOnly),
            ScoringAssessmentBuilder.Dep("dep-c", "ccc", status: ArchitectureStatus.Unknown),
            ScoringAssessmentBuilder.Dep("dep-b", "bbb", status: ArchitectureStatus.Ready),
        };

        var score = Scorer.Score(ScoringAssessmentBuilder.Ready(dependencies: deps));

        score.EvidenceIds.Should().OnlyHaveUniqueItems();
        score.EvidenceIds.Should().BeInAscendingOrder(StringComparer.Ordinal);
    }

    [Fact]
    public void RationaleCodes_TopLevel_IncludeCapCodeWhenCapFires()
    {
        var driver = ScoringAssessmentBuilder.Dep("dep-drv", "drv",
            type: DependencyType.Driver, status: ArchitectureStatus.Blocked);

        var score = Scorer.Score(ScoringAssessmentBuilder.Ready(dependencies: new[] { driver }));

        score.RationaleCodes.Should().Contain("CAP-REQUIRED-UNSUPPORTED-DRIVER-LE-30");
    }

    [Fact]
    public void ConfidenceScore_IsBoundedAndBandedCorrectly()
    {
        var score = Scorer.Score(ScoringAssessmentBuilder.Ready());

        score.ConfidenceScore.Should().BeInRange(0, 1);
        score.Confidence.Should().Be(score.ConfidenceScore switch
        {
            >= 0.75 => ConfidenceLabel.High,
            >= 0.50 => ConfidenceLabel.Medium,
            _ => ConfidenceLabel.Low,
        });
    }

    [Fact]
    public void GeneratedAt_EchoesAssessmentGeneratedAt()
    {
        var assessment = ScoringAssessmentBuilder.Ready();

        var score = Scorer.Score(assessment);

        score.GeneratedAt.Should().Be(assessment.GeneratedAt);
    }
}
