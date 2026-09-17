using MigrationPlanner.Application.Abstractions;
using MigrationPlanner.Application.Mcp;

namespace MigrationPlanner.Infrastructure.Mcp;

/// <summary>
/// Read-only registry that resolves only the nine allowlisted tool names.
/// Unknown names are rejected with <see cref="UnknownMcpToolException"/>.
/// </summary>
public sealed class MockMcpToolRegistry : IMcpToolRegistry
{
    private readonly IReadOnlyDictionary<string, IMcpTool> _tools;

    public MockMcpToolRegistry(IEnumerable<IMcpTool> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        var allowed = new HashSet<string>(McpToolNames.All, StringComparer.Ordinal);
        var lookup = new Dictionary<string, IMcpTool>(StringComparer.Ordinal);

        foreach (var tool in tools)
        {
            if (!allowed.Contains(tool.Name))
            {
                throw new InvalidOperationException(
                    $"Tool '{tool.Name}' is not on the read-only allowlist.");
            }

            if (!lookup.TryAdd(tool.Name, tool))
            {
                throw new InvalidOperationException(
                    $"Duplicate registration for tool '{tool.Name}'.");
            }
        }

        _tools = lookup;
    }

    public IReadOnlyCollection<string> AllowedTools => McpToolNames.All;

    public IMcpTool Resolve(string toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName) || !_tools.TryGetValue(toolName, out var tool))
        {
            throw new UnknownMcpToolException(toolName ?? string.Empty);
        }

        return tool;
    }

    public bool TryResolve(string toolName, out IMcpTool? tool)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            tool = null;
            return false;
        }

        if (_tools.TryGetValue(toolName, out var found))
        {
            tool = found;
            return true;
        }

        tool = null;
        return false;
    }
}
