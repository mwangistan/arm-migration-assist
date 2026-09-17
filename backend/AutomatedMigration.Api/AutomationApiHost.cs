using AutomatedMigration.Api.Configuration;
using AutomatedMigration.Api.Endpoints;
using AutomatedMigration.Api.Jobs;
using AutomatedMigration.Api.Repository;

namespace AutomatedMigration.Api;

public static class AutomationApiHost
{
    public const string CorsPolicyName = "AutomationFrontend";

    public static IServiceCollection AddAutomationApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AutomationOptions>(configuration.GetSection(AutomationOptions.SectionName));

        var options = configuration.GetSection(AutomationOptions.SectionName).Get<AutomationOptions>() ?? new AutomationOptions();
        var originsEnv = Environment.GetEnvironmentVariable("AUTOMATION_ALLOWED_ORIGINS");
        if (!string.IsNullOrWhiteSpace(originsEnv))
        {
            options.AllowedOrigins = originsEnv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        services.AddProblemDetails();
        if (options.AllowedOrigins.Length > 0)
        {
            services.AddCors(cors => cors.AddPolicy(CorsPolicyName, policy => policy
                .WithOrigins(options.AllowedOrigins)
                .WithMethods("GET", "POST", "OPTIONS")
                .WithHeaders("Content-Type")));
        }

        services.AddHttpClient<RepositoryFetcher>();
        services.AddSingleton<MigrationJobStore>();
        services.AddHostedService<MigrationJobWorker>();

        return services;
    }

    public static WebApplication UseAutomationApiPipeline(this WebApplication app, IConfiguration configuration)
    {
        var options = configuration.GetSection(AutomationOptions.SectionName).Get<AutomationOptions>() ?? new AutomationOptions();
        var originsEnv = Environment.GetEnvironmentVariable("AUTOMATION_ALLOWED_ORIGINS");
        if (!string.IsNullOrWhiteSpace(originsEnv))
        {
            options.AllowedOrigins = originsEnv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
        if (options.AllowedOrigins.Length > 0)
        {
            app.UseCors(CorsPolicyName);
        }
        return app;
    }

    public static IEndpointRouteBuilder MapAutomationApi(this IEndpointRouteBuilder endpoints, bool includeRootHealth = true)
    {
        if (includeRootHealth)
        {
            endpoints.MapGet("/health", () => Results.Ok(new { status = "ok" }));
        }
        endpoints.MapMigrationActionsEndpoints();
        return endpoints;
    }
}
