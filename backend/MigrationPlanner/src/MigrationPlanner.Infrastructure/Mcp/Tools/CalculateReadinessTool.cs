using System.Text.Json;
using MigrationPlanner.Application.Abstractions;
using MigrationPlanner.Application.Mcp;

namespace MigrationPlanner.Infrastructure.Mcp.Tools;

/// <summary>
/// Delegates to <see cref="IReadinessScorer"/> so the model can request a
/// deterministic score through MCP. Never invents scores itself.
/// </summary>
public sealed class CalculateReadinessTool : IMcpTool
{
    private readonly IReadinessScorer _scorer;

    public CalculateReadinessTool(IReadinessScorer scorer)
    {
        _scorer = scorer;
    }

    public string Name => McpToolNames.CalculateReadiness;

    public Task<McpToolResponse> InvokeAsync(McpToolRequest request, CancellationToken cancellationToken)
    {
        // Fixture layer will feed a real assessment; today we surface a shape-only stub.
        var payload = JsonDocument.Parse("{\"score\":null,\"note\":\"stub-until-fixtures\"}").RootElement;
        return Task.FromResult(McpToolResponse.Success(Name, payload));
    }
}
