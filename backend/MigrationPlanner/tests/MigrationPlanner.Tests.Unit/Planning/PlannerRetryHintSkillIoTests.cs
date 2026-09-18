using FluentAssertions;
using MigrationPlanner.Application.Abstractions;
using Xunit;

namespace MigrationPlanner.Tests.Unit.Planning;

// Unit tests for PlannerRetryHint.ForSkillIoMismatch. The heavier orchestrator flow
// (BuildRetryHint parsing PlanSafetyValidator violations end-to-end) is exercised via
// the integration retry surface; this file locks the record shape and reason routing.
public sealed class PlannerRetryHintSkillIoTests
{
    [Fact]
    public void ForSkillIoMismatch_PopulatesReasonAndPayloads()
    {
        var violations = new[]
        {
            new SkillIoViolation(0, "pipeline/github-actions-arm64-job", "inputs", "build-abc123"),
        };
        var allowlists = new Dictionary<string, SkillIoAllowlist>(StringComparer.Ordinal)
        {
            ["pipeline/github-actions-arm64-job"] = new SkillIoAllowlist(
                Inputs: new[] { "assessment.buildFindings" },
                Outputs: new[] { "patch.workflow" }),
        };

        var hint = PlannerRetryHint.ForSkillIoMismatch(
            violations, allowlists,
            diagnostic: "workItems[0].inputs cites 'build-abc123'",
            previousPlanJson: "{}");

        hint.Reason.Should().Be(PlannerRetryReason.SkillIoMismatch);
        hint.SkillIoViolations.Should().BeSameAs(violations);
        hint.SkillIoAllowlists.Should().BeSameAs(allowlists);
        hint.PreviousPlanJson.Should().Be("{}");
        hint.Diagnostic.Should().Contain("build-abc123");
    }

    [Fact]
    public void SkillIoViolation_IsValueRecord()
    {
        var a = new SkillIoViolation(0, "s", "inputs", "x");
        var b = new SkillIoViolation(0, "s", "inputs", "x");
        a.Should().Be(b);
    }

    [Fact]
    public void SkillIoAllowlist_IsValueRecordButListReferenceMatters()
    {
        // Records use structural equality on the reference-typed IReadOnlyList fields, so lists
        // that are separate instances with the same contents will NOT compare equal. Documenting
        // the semantics rather than asserting deep equality: callers should build one allowlist
        // instance per skill and share the reference.
        var inputs = new[] { "a" };
        var outputs = new[] { "b" };
        var a = new SkillIoAllowlist(inputs, outputs);
        var b = new SkillIoAllowlist(inputs, outputs);
        a.Should().Be(b);
    }
}
