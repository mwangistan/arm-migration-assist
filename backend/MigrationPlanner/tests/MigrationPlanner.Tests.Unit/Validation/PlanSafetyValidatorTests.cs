using System.Text.Json;
using FluentAssertions;
using MigrationPlanner.Application.Abstractions;
using MigrationPlanner.Domain.Assessment;
using MigrationPlanner.Domain.Errors;
using MigrationPlanner.Domain.Guidance;
using MigrationPlanner.Domain.Plan;
using MigrationPlanner.Infrastructure.Scoring;
using MigrationPlanner.Infrastructure.Validation;
using MigrationPlanner.Tests.Unit.Scoring;
using Xunit;

namespace MigrationPlanner.Tests.Unit.Validation;

public sealed class PlanSafetyValidatorTests
{
    private static readonly DeterministicReadinessScorer Scorer = new();

    [Fact]
    public void ValidPlan_WithMatchingDigestAndKnownEvidence_ReturnsOk()
    {
        var (assessment, score, plan) = BuildValidCase();
        var validator = BuildValidator();

        var result = validator.Validate(plan, assessment, score);

        result.IsSafe.Should().BeTrue(result.Violations.Any() ? result.Violations[0] : "");
        result.ErrorCode.Should().BeNull();
    }

    [Fact]
    public void MismatchedScoreDigest_ReturnsScoreDigestMismatchError()
    {
        var (assessment, score, plan) = BuildValidCase(digestOverride: new string('0', 64));
        var validator = BuildValidator();

        var result = validator.Validate(plan, assessment, score);

        result.IsSafe.Should().BeFalse();
        result.ErrorCode.Should().Be(PlannerErrorCode.PlanScoreDigestMismatch);
    }

    [Fact]
    public void MissingScoreDigest_ReturnsShapeInvalidError()
    {
        var (assessment, score, plan) = BuildValidCase(omitKey: "scoreDigest");
        var validator = BuildValidator();

        var result = validator.Validate(plan, assessment, score);

        result.IsSafe.Should().BeFalse();
        result.ErrorCode.Should().Be(PlannerErrorCode.PlanShapeInvalid);
    }

    [Fact]
    public void UnknownEvidenceIdCitation_ReturnsEvidenceMissingError()
    {
        var (assessment, score, plan) = BuildValidCase(extraEvidenceCitation: "does-not-exist");
        var validator = BuildValidator();

        var result = validator.Validate(plan, assessment, score);

        result.IsSafe.Should().BeFalse();
        result.ErrorCode.Should().Be(PlannerErrorCode.PlanEvidenceMissing);
        result.Violations.Should().ContainMatch("*does-not-exist*");
    }

    [Fact]
    public void UnknownGuidanceIdCitation_ReturnsGuidanceMissingError()
    {
        var (assessment, score, plan) = BuildValidCase(extraGuidanceCitation: "hallucinated-guidance-99");
        var validator = BuildValidator();

        var result = validator.Validate(plan, assessment, score);

        result.IsSafe.Should().BeFalse();
        result.ErrorCode.Should().Be(PlannerErrorCode.PlanGuidanceMissing);
        result.Violations.Should().ContainMatch("*hallucinated-guidance-99*");
    }

    [Fact]
    public void UnsafeInstruction_ReturnsSafetyViolationError()
    {
        var (assessment, score, plan) = BuildValidCase(unsafeText: "run git push origin main after commit");
        var validator = BuildValidator();

        var result = validator.Validate(plan, assessment, score);

        result.IsSafe.Should().BeFalse();
        result.ErrorCode.Should().Be(PlannerErrorCode.PlanSafetyViolation);
    }

    [Fact]
    public void MismatchedAssessmentId_ReturnsSafetyViolationError()
    {
        var (assessment, score, plan) = BuildValidCase();
        plan = plan with { AssessmentId = "wrong-assessment-id" };
        var validator = BuildValidator();

        var result = validator.Validate(plan, assessment, score);

        result.IsSafe.Should().BeFalse();
        result.ErrorCode.Should().Be(PlannerErrorCode.PlanSafetyViolation);
    }

    [Fact]
    public void RecommendedPathDoesNotMatchDispatch_ReturnsRecommendationInconsistent()
    {
        var (assessment, score, plan) = BuildValidCase(recommendedPathOverride: "arm64ec");
        var validator = BuildValidator();

        var result = validator.Validate(plan, assessment, score);

        result.IsSafe.Should().BeFalse();
        result.ErrorCode.Should().Be(PlannerErrorCode.PlanRecommendationInconsistent);
        result.Violations.Should().ContainMatch("*arm64ec*");
        result.Violations.Should().ContainMatch("*native-arm64*");
    }

    private static PlanSafetyValidator BuildValidator() =>
        new(new EmptyGuidanceStore());

    private static (RepositoryAssessmentV1 assessment, ReadinessScoreV1 score, MigrationPlanV1 plan) BuildValidCase(
        string? digestOverride = null,
        string? omitKey = null,
        string? extraEvidenceCitation = null,
        string? extraGuidanceCitation = null,
        string? unsafeText = null,
        string? recommendedPathOverride = null)
    {
        var deps = new[]
        {
            ScoringAssessmentBuilder.Dep("dep-known", "Sample.Utilities"),
        };
        var assessment = ScoringAssessmentBuilder.Ready(dependencies: deps);
        var score = Scorer.Score(assessment);
        var digest = digestOverride
            ?? MigrationPlanner.Infrastructure.Model.ScoreDigest.Compute(score);

        var payload = new Dictionary<string, object?>
        {
            ["schemaVersion"] = "1.0",
            ["planId"] = "plan-test-001",
            ["assessmentId"] = assessment.AssessmentId,
            ["generatedAt"] = "2026-09-16T00:00:00Z",
            ["modelProvenance"] = new { provider = "fake", name = "fake-planner", version = "0.1.0" },
            ["scoreDigest"] = digest,
            ["corpusVersion"] = "2026-09-15.1",
            ["recommendedPath"] = recommendedPathOverride ?? "native-arm64",
            ["confidence"] = "high",
            ["executiveSummary"] = unsafeText ?? "Baseline plan.",
            ["scoreInterpretation"] = "All dimensions clean.",
            ["facts"] = new[]
            {
                new
                {
                    statement = "Deterministic scorer observed evidence.",
                    evidenceIds = extraEvidenceCitation is not null
                        ? new[] { "dep-known", extraEvidenceCitation }
                        : new[] { "dep-known" },
                    guidanceIds = extraGuidanceCitation is not null
                        ? new[] { extraGuidanceCitation }
                        : Array.Empty<string>(),
                },
            },
            ["inferences"] = Array.Empty<object>(),
            ["alternatives"] = new[]
            {
                new
                {
                    path = "native-arm64",
                    disposition = "viable",
                    rationale = "Recommended path.",
                    evidenceIds = Array.Empty<string>(),
                    guidanceIds = Array.Empty<string>(),
                },
            },
            ["workItems"] = Array.Empty<object>(),
            ["missingSkills"] = Array.Empty<object>(),
            ["validationPlan"] = new { objectives = Array.Empty<object>(), checks = Array.Empty<object>() },
            ["risks"] = Array.Empty<object>(),
            ["unknowns"] = Array.Empty<object>(),
            ["requiredApprovals"] = Array.Empty<object>(),
            ["reusableOutputs"] = Array.Empty<object>(),
        };

        if (omitKey is not null)
        {
            payload.Remove(omitKey);
        }

        var json = JsonSerializer.Serialize(payload);
        var plan = JsonSerializer.Deserialize<MigrationPlanV1>(json)!;
        return (assessment, score, plan);
    }

    private sealed class EmptyGuidanceStore : IWindowsOnArmGuidanceStore
    {
        public string CorpusVersion => "2026-09-15.1";

        public IReadOnlyCollection<GuidanceSnippet> All() => Array.Empty<GuidanceSnippet>();

        public IReadOnlyCollection<GuidanceSnippet> FindByTopic(Topic topic) => Array.Empty<GuidanceSnippet>();

        public bool TryGet(string guidanceId, out GuidanceSnippet? snippet)
        {
            snippet = null;
            return false;
        }
    }
}
