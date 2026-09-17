using FluentAssertions;
using MigrationPlanner.Application.Abstractions;
using MigrationPlanner.Domain.Guidance;
using MigrationPlanner.Infrastructure.Model;
using MigrationPlanner.Infrastructure.Scoring;
using MigrationPlanner.Tests.Unit.Scoring;
using Xunit;

namespace MigrationPlanner.Tests.Unit.Model;

public sealed class PlannerPromptBuilderTests
{
    [Fact]
    public void SystemPrompt_UsesDeclaredPipelineSkillContract()
    {
        var prompt = PlannerPromptBuilder.BuildSystemPrompt();

        prompt.Should().Contain("Skill I/O honesty");
        prompt.Should().Contain("\"agentOrSkill\": \"pipeline/github-actions-arm64-job\"");
        prompt.Should().Contain("\"inputs\": [\"repository\"]");
        prompt.Should().Contain("\"expectedOutputs\": [\"patch\"]");
        prompt.Should().NotContain("\"agentOrSkill\": \"build/add-ci-job\"");
        prompt.Should().NotContain("\"expectedOutputs\": [\"workflow-yaml\"]");
    }

    [Fact]
    public void UserPrompt_SkillIoRetry_ListsExactAvailableContracts()
    {
        var assessment = ScoringAssessmentBuilder.Ready();
        var score = new DeterministicReadinessScorer().Score(assessment);
        var hint = PlannerRetryHint.ForSkillIoMismatch(
            [
                new PlannerSkillIoContract(
                    "pipeline/github-actions-arm64-job",
                    "Generates a reviewable CI patch.",
                    true,
                    ["repository"],
                    ["patch"]),
            ],
            "workItem inputs must be a subset of the skill's declared inputs.",
            "{\"schemaVersion\":\"1.0\"}");

        var prompt = PlannerPromptBuilder.BuildUserPrompt(
            assessment,
            score,
            new NoopGuidance(),
            new PlannerProvenance("hosted", "test-model", "1.0"),
            hint);

        prompt.Should().Contain("Exact assessment.availableSkills contracts:");
        prompt.Should().Contain("supportedInputs: [\"repository\"]");
        prompt.Should().Contain("supportedOutputs: [\"patch\"]");
        prompt.Should().Contain("Correct every mismatched work item");
        prompt.Should().Contain("Never use");
        prompt.Should().Contain("a read-only assessment skill for patch-producing migration work");
    }

    private sealed class NoopGuidance : IGuidanceLookup
    {
        public string CorpusVersion => "2026-09-15.1";
        public int RemainingBudget => 0;
        public IReadOnlyCollection<string> RetrievedGuidanceIds => [];
        public IReadOnlyList<GuidanceIndexEntry> ListIndex() => [];
        public Task<GuidanceSnippet?> LookupByIdAsync(string guidanceId, CancellationToken cancellationToken) =>
            Task.FromResult<GuidanceSnippet?>(null);
        public Task<IReadOnlyList<GuidanceSnippet>> LookupByTopicAsync(Topic topic, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<GuidanceSnippet>>([]);
    }
}
