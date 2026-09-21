using MigrationPlanner.Api.Automation;
using MigrationPlanner.Api.Configuration;
using MigrationPlanner.Api.Endpoints;
using MigrationPlanner.Api.Planning;
using MigrationPlanner.Application.Abstractions;
using MigrationPlanner.Infrastructure.DependencyInjection;
using MigrationPlanner.Infrastructure.Model;

namespace MigrationPlanner.Api;

public static class MigrationPlannerHost
{
    public static IServiceCollection AddMigrationPlannerApi(this IServiceCollection services, IConfiguration configuration, string contentRoot)
    {
        var options = ResolveOptions(configuration, contentRoot);
        services.AddSingleton(options);
        services.AddSingleton<PlanRunStore>();
        services.AddProblemDetails();

        if (options.AllowedOrigins.Length > 0)
        {
            services.AddCors(cors => cors.AddPolicy(PlannerOptions.CorsPolicyName, policy => policy
                .WithOrigins(options.AllowedOrigins)
                .WithMethods("GET", "POST", "OPTIONS")
                .WithHeaders("Content-Type")));
        }

        var (phi, hosted) = ResolveModelOptions(configuration, options.ModelProvider);
        services.AddMigrationPlannerInfrastructure(
            corpusOptions => corpusOptions.CorpusRoot = options.CorpusRoot,
            options.ModelProvider,
            phi,
            hosted);

        var automationOptions = new AutomationApiOptions();
        configuration.GetSection(AutomationApiOptions.SectionName).Bind(automationOptions);
        var automationUrlEnv = Environment.GetEnvironmentVariable("MIGRATIONPLANNER_AUTOMATION_API_URL");
        if (!string.IsNullOrWhiteSpace(automationUrlEnv)) automationOptions.BaseUrl = automationUrlEnv;
        services.AddSingleton(automationOptions);

        if (!string.IsNullOrWhiteSpace(automationOptions.BaseUrl))
        {
            services.AddHttpClient<IAutomationDispatcher, HttpAutomationDispatcher>(client =>
            {
                client.BaseAddress = new Uri(automationOptions.BaseUrl!, UriKind.Absolute);
                client.Timeout = TimeSpan.FromSeconds(Math.Max(1, automationOptions.TimeoutSeconds));
                client.DefaultRequestHeaders.UserAgent.ParseAdd("arm-migration-assist-planner-api/1.0");
            });
        }
        else
        {
            services.AddSingleton<IAutomationDispatcher, NoopAutomationDispatcher>();
        }

        return services;
    }

    // Force construction of critical singletons so misconfiguration fails startup rather than the first request.
    public static WebApplication UseMigrationPlannerPipeline(this WebApplication app)
    {
        _ = app.Services.GetRequiredService<IWindowsOnArmGuidanceStore>();
        _ = app.Services.GetRequiredService<IPlannerModel>();

        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.Headers.CacheControl = "no-store";
            }
            await next(context);
        });

        var options = app.Services.GetRequiredService<PlannerOptions>();
        if (options.AllowedOrigins.Length > 0)
        {
            app.UseCors(PlannerOptions.CorsPolicyName);
        }
        return app;
    }

    public static IEndpointRouteBuilder MapMigrationPlannerApi(this IEndpointRouteBuilder endpoints, bool includeRootHealth = true)
    {
        endpoints.MapMigrationPlansEndpoint();
        endpoints.MapMigrationPlanRunsEndpoint();
        endpoints.MapMigrationReportsEndpoint();
        if (includeRootHealth)
        {
            endpoints.MapGet("/health", () => Results.Ok(new { status = "ok" }));
        }
        return endpoints;
    }

    internal static PlannerOptions ResolveOptions(IConfiguration configuration, string contentRoot)
    {
        var options = new PlannerOptions();
        configuration.GetSection(PlannerOptions.SectionName).Bind(options);
        options.ModelProvider = PlannerProviderResolver.Resolve(configuration);

        var corpusRootEnv = Environment.GetEnvironmentVariable("MIGRATIONPLANNER_CORPUS_ROOT");
        if (!string.IsNullOrWhiteSpace(corpusRootEnv))
        {
            options.CorpusRoot = corpusRootEnv;
        }
        if (string.IsNullOrWhiteSpace(options.CorpusRoot))
        {
            options.CorpusRoot = ResolveDefaultCorpusRoot(contentRoot);
        }

        var originsEnv = Environment.GetEnvironmentVariable("MIGRATIONPLANNER_ALLOWED_ORIGINS");
        if (!string.IsNullOrWhiteSpace(originsEnv))
        {
            options.AllowedOrigins = originsEnv
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
        return options;
    }

    private static (PhiModelOptions? Phi, HostedModelOptions? Hosted) ResolveModelOptions(IConfiguration configuration, PlannerModelProvider provider)
    {
        PhiModelOptions? phi = null;
        HostedModelOptions? hosted = null;

        if (provider == PlannerModelProvider.Phi)
        {
            phi = new PhiModelOptions();
            configuration.GetSection(PhiModelOptions.SectionName).Bind(phi);

            var endpointEnv = Environment.GetEnvironmentVariable("MIGRATIONPLANNER_PHI_ENDPOINT");
            if (!string.IsNullOrWhiteSpace(endpointEnv)) phi.Endpoint = endpointEnv;
            var deploymentEnv = Environment.GetEnvironmentVariable("MIGRATIONPLANNER_PHI_DEPLOYMENT");
            if (!string.IsNullOrWhiteSpace(deploymentEnv)) phi.DeploymentName = deploymentEnv;
            var keyEnv = Environment.GetEnvironmentVariable("MIGRATIONPLANNER_PHI_API_KEY");
            if (!string.IsNullOrWhiteSpace(keyEnv)) phi.ApiKey = keyEnv;
        }
        else if (provider == PlannerModelProvider.Hosted)
        {
            hosted = new HostedModelOptions();
            configuration.GetSection(HostedModelOptions.SectionName).Bind(hosted);

            var endpointEnv = Environment.GetEnvironmentVariable("MIGRATIONPLANNER_HOSTED_ENDPOINT");
            if (!string.IsNullOrWhiteSpace(endpointEnv)) hosted.Endpoint = endpointEnv;
            var deploymentEnv = Environment.GetEnvironmentVariable("MIGRATIONPLANNER_HOSTED_DEPLOYMENT");
            if (!string.IsNullOrWhiteSpace(deploymentEnv)) hosted.DeploymentName = deploymentEnv;

            // Bicep in infra/main.bicep currently emits MIGRATIONPLANNER_LLM_* names; accept them as a fallback so the container app doesn't need to be reconfigured before the next infra deploy.
            if (string.IsNullOrWhiteSpace(hosted.Endpoint))
            {
                var llmEndpoint = Environment.GetEnvironmentVariable("MIGRATIONPLANNER_LLM_ENDPOINT");
                if (!string.IsNullOrWhiteSpace(llmEndpoint)) hosted.Endpoint = llmEndpoint;
            }
            if (string.IsNullOrWhiteSpace(hosted.DeploymentName))
            {
                var llmDeployment = Environment.GetEnvironmentVariable("MIGRATIONPLANNER_LLM_DEPLOYMENT");
                if (!string.IsNullOrWhiteSpace(llmDeployment)) hosted.DeploymentName = llmDeployment;
            }

            var keyEnv = Environment.GetEnvironmentVariable("MIGRATIONPLANNER_HOSTED_API_KEY");
            if (!string.IsNullOrWhiteSpace(keyEnv)) hosted.ApiKey = keyEnv;

            var toolEnv = Environment.GetEnvironmentVariable("MIGRATIONPLANNER_HOSTED_ENABLE_GUIDANCE_LOOKUP_TOOL");
            if (!string.IsNullOrWhiteSpace(toolEnv) && bool.TryParse(toolEnv, out var toolFlag))
            {
                hosted.EnableGuidanceLookupTool = toolFlag;
            }
        }

        return (phi, hosted);
    }

    internal static string ResolveDefaultCorpusRoot(string contentRoot)
    {
        var current = new DirectoryInfo(contentRoot);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "knowledge", "windows-on-arm");
            if (File.Exists(Path.Combine(candidate, "corpus.json")))
            {
                return candidate;
            }
            current = current.Parent;
        }
        return Path.Combine(contentRoot, "knowledge", "windows-on-arm");
    }
}
