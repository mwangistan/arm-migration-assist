namespace MigrationPlanner.Application.Mcp;

public sealed class UnknownMcpToolException : Exception
{
    public UnknownMcpToolException(string toolName)
        : base($"MCP tool '{toolName}' is not on the read-only allowlist.")
    {
        ToolName = toolName;
    }

    public string ToolName { get; }
}
