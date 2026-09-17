namespace MigrationPlanner.Application.Mcp;

/// <summary>
/// String constants for the nine allowlisted read-only MCP tools. This list is
/// the sole source of truth used by <c>IMcpToolRegistry</c> to reject unknown
/// tool names.
/// </summary>
public static class McpToolNames
{
    public const string GetRepositorySummary = "get_repository_summary";
    public const string GetDependencyFindings = "get_dependency_findings";
    public const string GetCodeCompatibilityFindings = "get_code_compatibility_findings";
    public const string GetBuildReadinessFindings = "get_build_readiness_findings";
    public const string GetWindowsExperienceFindings = "get_windows_experience_findings";
    public const string GetScanCoverage = "get_scan_coverage";
    public const string GetAvailableSkillCatalog = "get_available_skill_catalog";
    public const string CalculateReadiness = "calculate_readiness";
    public const string LookupWindowsArmGuidance = "lookup_windows_arm_guidance";

    public static readonly IReadOnlyCollection<string> All =
    [
        GetRepositorySummary,
        GetDependencyFindings,
        GetCodeCompatibilityFindings,
        GetBuildReadinessFindings,
        GetWindowsExperienceFindings,
        GetScanCoverage,
        GetAvailableSkillCatalog,
        CalculateReadiness,
        LookupWindowsArmGuidance,
    ];
}
