using System.Text.Json;
using MigrationPlanner.Application.Abstractions;
using MigrationPlanner.Application.Mcp;

namespace MigrationPlanner.Infrastructure.Mcp.Tools;

internal static class EmptyToolResponse
{
    public static Task<McpToolResponse> ForAsync(string toolName, string emptyArrayProperty)
    {
        var json = JsonDocument.Parse($"{{\"{emptyArrayProperty}\":[]}}").RootElement;
        return Task.FromResult(McpToolResponse.Success(toolName, json));
    }
}

public sealed class GetRepositorySummaryTool : IMcpTool
{
    public string Name => McpToolNames.GetRepositorySummary;

    public Task<McpToolResponse> InvokeAsync(McpToolRequest request, CancellationToken cancellationToken)
    {
        var json = JsonDocument.Parse("{\"summary\":null}").RootElement;
        return Task.FromResult(McpToolResponse.Success(Name, json));
    }
}

public sealed class GetDependencyFindingsTool : IMcpTool
{
    public string Name => McpToolNames.GetDependencyFindings;

    public Task<McpToolResponse> InvokeAsync(McpToolRequest request, CancellationToken cancellationToken) =>
        EmptyToolResponse.ForAsync(Name, "dependencies");
}

public sealed class GetCodeCompatibilityFindingsTool : IMcpTool
{
    public string Name => McpToolNames.GetCodeCompatibilityFindings;

    public Task<McpToolResponse> InvokeAsync(McpToolRequest request, CancellationToken cancellationToken) =>
        EmptyToolResponse.ForAsync(Name, "codeFindings");
}

public sealed class GetBuildReadinessFindingsTool : IMcpTool
{
    public string Name => McpToolNames.GetBuildReadinessFindings;

    public Task<McpToolResponse> InvokeAsync(McpToolRequest request, CancellationToken cancellationToken)
    {
        var json = JsonDocument.Parse("{\"buildFindings\":null}").RootElement;
        return Task.FromResult(McpToolResponse.Success(Name, json));
    }
}

public sealed class GetWindowsExperienceFindingsTool : IMcpTool
{
    public string Name => McpToolNames.GetWindowsExperienceFindings;

    public Task<McpToolResponse> InvokeAsync(McpToolRequest request, CancellationToken cancellationToken)
    {
        var json = JsonDocument.Parse("{\"windowsExperience\":null}").RootElement;
        return Task.FromResult(McpToolResponse.Success(Name, json));
    }
}

public sealed class GetScanCoverageTool : IMcpTool
{
    public string Name => McpToolNames.GetScanCoverage;

    public Task<McpToolResponse> InvokeAsync(McpToolRequest request, CancellationToken cancellationToken)
    {
        var json = JsonDocument.Parse("{\"scanCoverage\":null}").RootElement;
        return Task.FromResult(McpToolResponse.Success(Name, json));
    }
}

public sealed class GetAvailableSkillCatalogTool : IMcpTool
{
    public string Name => McpToolNames.GetAvailableSkillCatalog;

    public Task<McpToolResponse> InvokeAsync(McpToolRequest request, CancellationToken cancellationToken) =>
        EmptyToolResponse.ForAsync(Name, "availableSkills");
}
