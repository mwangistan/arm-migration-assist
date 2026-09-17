using FluentAssertions;
using MigrationPlanner.Application.Abstractions;
using MigrationPlanner.Application.Mcp;
using MigrationPlanner.Infrastructure.Mcp;
using MigrationPlanner.Infrastructure.Mcp.Tools;
using Xunit;

namespace MigrationPlanner.Tests.Unit.Mcp;

public sealed class MockMcpToolRegistryTests
{
    [Fact]
    public void Resolve_AllowlistedTool_ReturnsTool()
    {
        var registry = BuildRegistry();

        var tool = registry.Resolve(McpToolNames.GetRepositorySummary);

        tool.Should().NotBeNull();
        tool.Name.Should().Be(McpToolNames.GetRepositorySummary);
    }

    [Fact]
    public void Resolve_UnknownTool_ThrowsUnknownMcpToolException()
    {
        var registry = BuildRegistry();

        var act = () => registry.Resolve("delete_all_the_things");

        act.Should().Throw<UnknownMcpToolException>();
    }

    [Fact]
    public void AllowedTools_ContainsExactlyNineNames()
    {
        var registry = BuildRegistry();

        registry.AllowedTools.Should().HaveCount(9);
        registry.AllowedTools.Should().Contain(McpToolNames.LookupWindowsArmGuidance);
        registry.AllowedTools.Should().Contain(McpToolNames.CalculateReadiness);
    }

    [Fact]
    public void TryResolve_WhitespaceName_ReturnsFalse()
    {
        var registry = BuildRegistry();

        var result = registry.TryResolve("   ", out var tool);

        result.Should().BeFalse();
        tool.Should().BeNull();
    }

    private static IMcpToolRegistry BuildRegistry()
    {
        var scorer = new StubScorer();
        var store = new StubGuidanceStore();
        var tools = new IMcpTool[]
        {
            new GetRepositorySummaryTool(),
            new GetDependencyFindingsTool(),
            new GetCodeCompatibilityFindingsTool(),
            new GetBuildReadinessFindingsTool(),
            new GetWindowsExperienceFindingsTool(),
            new GetScanCoverageTool(),
            new GetAvailableSkillCatalogTool(),
            new CalculateReadinessTool(scorer),
            new LookupWindowsArmGuidanceTool(store),
        };

        return new MockMcpToolRegistry(tools);
    }

    private sealed class StubScorer : IReadinessScorer
    {
        public MigrationPlanner.Domain.Plan.ReadinessScoreV1 Score(
            MigrationPlanner.Domain.Assessment.RepositoryAssessmentV1 assessment) =>
            new()
            {
                AssessmentId = assessment.AssessmentId,
                GeneratedAt = assessment.GeneratedAt,
            };
    }

    private sealed class StubGuidanceStore : IWindowsOnArmGuidanceStore
    {
        public string CorpusVersion => "test-corpus";

        public IReadOnlyCollection<MigrationPlanner.Domain.Guidance.GuidanceSnippet> All() =>
            Array.Empty<MigrationPlanner.Domain.Guidance.GuidanceSnippet>();

        public IReadOnlyCollection<MigrationPlanner.Domain.Guidance.GuidanceSnippet> FindByTopic(
            MigrationPlanner.Domain.Guidance.Topic topic) =>
            Array.Empty<MigrationPlanner.Domain.Guidance.GuidanceSnippet>();

        public bool TryGet(string guidanceId, out MigrationPlanner.Domain.Guidance.GuidanceSnippet? snippet)
        {
            snippet = null;
            return false;
        }
    }
}
