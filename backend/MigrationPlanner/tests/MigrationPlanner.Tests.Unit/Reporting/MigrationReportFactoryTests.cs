using FluentAssertions;
using MigrationPlanner.Application.Planning;
using MigrationPlanner.Application.Reporting;
using MigrationPlanner.Domain.Plan;
using MigrationPlanner.Tests.Unit.Scoring;
using Xunit;

namespace MigrationPlanner.Tests.Unit.Reporting;

public sealed class MigrationReportFactoryTests
{
    [Fact]
    public void From_LiftsRepositoryIdentityAndPreservesArtifacts()
    {
        var assessment = ScoringAssessmentBuilder.Ready(assessmentId: "asm-1");
        var plan = new MigrationPlanV1 { AssessmentId = assessment.AssessmentId };
        var score = new DeterministicReadinessScorerHarness().Score(assessment);
        var warnings = new[] { "provisional" };
        var result = PlanResult.Ok(plan, score, "run-1", warnings);

        var report = MigrationReportFactory.From(assessment, result);

        report.RunId.Should().Be("run-1");
        report.AssessmentId.Should().Be("asm-1");
        report.Plan.Should().BeSameAs(plan);
        report.Score.Should().BeSameAs(score);
        report.Warnings.Should().BeEquivalentTo(warnings);
        report.Repository.Name.Should().Be(assessment.Repository.Name);
        report.Repository.CommitSha.Should().Be(assessment.Repository.CommitSha);
        report.Repository.DefaultBranch.Should().Be(assessment.Repository.DefaultBranch);
        report.Repository.Url.Should().Be(assessment.Repository.Url);
        report.Repository.License.Should().Be(assessment.Repository.License);
    }

    [Fact]
    public void From_FailedResult_Throws()
    {
        var assessment = ScoringAssessmentBuilder.Ready();
        var result = PlanResult.Fail("Planner.SchemaInvalid", "run-x", "bad input");

        var act = () => MigrationReportFactory.From(assessment, result);

        act.Should().Throw<InvalidOperationException>();
    }
}

file sealed class DeterministicReadinessScorerHarness
{
    private readonly MigrationPlanner.Infrastructure.Scoring.DeterministicReadinessScorer _inner = new();

    public ReadinessScoreV1 Score(MigrationPlanner.Domain.Assessment.RepositoryAssessmentV1 assessment) =>
        _inner.Score(assessment);
}
