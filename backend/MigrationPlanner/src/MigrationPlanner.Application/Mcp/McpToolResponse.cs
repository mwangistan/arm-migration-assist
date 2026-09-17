using System.Text.Json;

namespace MigrationPlanner.Application.Mcp;

public sealed record McpToolResponse
{
    public string ToolName { get; init; } = string.Empty;
    public bool IsSuccess { get; init; }
    public JsonElement? Result { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }

    public static McpToolResponse Success(string toolName, JsonElement result) =>
        new() { ToolName = toolName, IsSuccess = true, Result = result };

    public static McpToolResponse Error(string toolName, string errorCode, string message) =>
        new() { ToolName = toolName, IsSuccess = false, ErrorCode = errorCode, ErrorMessage = message };
}
