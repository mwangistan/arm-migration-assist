using ArmMigrationAssist.RepositoryWorkspace;
using AutomatedMigration.Api.Configuration;
using AutomatedMigration.Api.Endpoints;
using AutomatedMigration.Api.Jobs;
using AutomatedMigration.Api.Validation;

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

        services.AddRepositoryClonePool(configuration);
        services.AddHttpClient();

        var validationOpts = new ValidationDispatcherOptions();
        configuration.GetSection(ValidationDispatcherOptions.SectionName).Bind(validationOpts);
        var validationUrlEnv = Environment.GetEnvironmentVariable("AUTOMATION_VALIDATION_API_URL");
        if (!string.IsNullOrWhiteSpace(validationUrlEnv)) validationOpts.BaseUrl = validationUrlEnv;
        services.AddSingleton(validationOpts);

        if (!string.IsNullOrWhiteSpace(validationOpts.BaseUrl))
        {
            services.AddHttpClient<IValidationDispatcher, HttpValidationDispatcher>(client =>
            {
                client.BaseAddress = new Uri(validationOpts.BaseUrl!, UriKind.Absolute);
                client.Timeout = TimeSpan.FromSeconds(Math.Max(5, validationOpts.TimeoutSeconds));
                client.DefaultRequestHeaders.UserAgent.ParseAdd("arm-migration-assist-automation-api/1.0");
            });
        }
        else
        {
            services.AddSingleton<IValidationDispatcher, NoopValidationDispatcher>();
        }

        var arm64Opts = new Arm64BuildDispatcherOptions();
        configuration.GetSection(Arm64BuildDispatcherOptions.SectionName).Bind(arm64Opts);
        var arm64UrlEnv = Environment.GetEnvironmentVariable("ARM64_RUNNER_URL");
        if (!string.IsNullOrWhiteSpace(arm64UrlEnv)) arm64Opts.BaseUrl = arm64UrlEnv;
        var arm64TokenEnv = Environment.GetEnvironmentVariable("ARM64_RUNNER_BEARER_TOKEN");
        if (!string.IsNullOrWhiteSpace(arm64TokenEnv)) arm64Opts.BearerToken = arm64TokenEnv;
        services.AddSingleton<Arm64BuildDispatcherOptions>(arm64Opts);

        if (!string.IsNullOrWhiteSpace(arm64Opts.BaseUrl))
        {
            services.AddHttpClient<IArm64BuildDispatcher, HttpArm64BuildDispatcher>(client =>
            {
                client.BaseAddress = new Uri(arm64Opts.BaseUrl!, UriKind.Absolute);
                client.Timeout = TimeSpan.FromSeconds(Math.Max(5, arm64Opts.TimeoutSeconds));
                client.DefaultRequestHeaders.UserAgent.ParseAdd("arm-migration-assist-automation-api/1.0");
            });
        }
        else
        {
            services.AddSingleton<IArm64BuildDispatcher, NoopArm64BuildDispatcher>();
        }

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