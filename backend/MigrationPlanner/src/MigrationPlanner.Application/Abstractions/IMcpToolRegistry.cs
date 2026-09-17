using MigrationPlanner.Application.Mcp;

namespace MigrationPlanner.Application.Abstractions;

/// <summary>
/// Read-only allowlist for the mock MCP server. Unknown tool names must be
/// rejected with <see cref="UnknownMcpToolException"/>.
/// </summary>
public interface IMcpToolRegistry
{
    IReadOnlyCollection<string> AllowedTools { get; }

    IMcpTool Resolve(string toolName);

    bool TryResolve(string toolName, out IMcpTool? tool);
}
