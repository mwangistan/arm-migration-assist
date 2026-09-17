using MigrationPlanner.Infrastructure.DependencyInjection;

namespace MigrationPlanner.Api.Configuration;

public sealed class PlannerOptions
{
    public const string SectionName = "Planner";

    public const string CorsPolicyName = "PlannerFrontend";

    /// <summary>
    /// Absolute or relative path to the Windows on Arm guidance corpus root
    /// containing <c>corpus.json</c> and its snippet files.
    /// </summary>
    public string CorpusRoot { get; set; } = string.Empty;

    /// <summary>
    /// Which <see cref="MigrationPlanner.Application.Abstractions.IPlannerModel"/>
    /// implementation to register. Read from the
    /// <c>MIGRATIONPLANNER_MODEL_PROVIDER</c> environment variable.
    /// </summary>
    public PlannerModelProvider ModelProvider { get; set; } = PlannerModelProvider.Fake;

    /// <summary>
    /// Origins the browser-based frontend may call the API from. An empty list
    /// disables CORS entirely (server-to-server clients only).
    /// Override with <c>MIGRATIONPLANNER_ALLOWED_ORIGINS</c> (comma-separated).
    /// </summary>
    public string[] AllowedOrigins { get; set; } = Array.Empty<string>();
}
