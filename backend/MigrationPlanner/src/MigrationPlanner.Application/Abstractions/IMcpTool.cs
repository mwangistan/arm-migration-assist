using MigrationPlanner.Application.Mcp;

namespace MigrationPlanner.Application.Abstractions;

/// <summary>
/// A single read-only MCP tool. Implementations must not modify a repository,
/// execute shells, or reach the network.
/// </summary>
public interface IMcpTool
{
    string Name { get; }

    Task<McpToolResponse> InvokeAsync(McpToolRequest request, CancellationToken cancellationToken);
}
