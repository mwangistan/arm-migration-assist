using System.Text.Json;

namespace MigrationPlanner.Application.Mcp;

public sealed record McpToolRequest(
    string ToolName,
    JsonElement Arguments,
    string? RunId = null);
