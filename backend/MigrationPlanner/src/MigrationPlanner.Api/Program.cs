using MigrationPlanner.Api.Configuration;
using MigrationPlanner.Api.Endpoints;
using MigrationPlanner.Application.Abstractions;
using MigrationPlanner.Infrastructure.DependencyInjection;
using MigrationPlanner.Infrastructure.Model;

namespace MigrationPlanner.Api;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        var options = new PlannerOptions();
        builder.Configuration.GetSection(PlannerOptions.SectionName).Bind(options);

        var providerEnv = Environment.GetEnvironmentVariable("MIGRATIONPLANNER_MODEL_PROVIDER");
        if (!string.IsNullOrWhiteSpace(providerEnv) &&
            Enum.TryParse<PlannerModelProvider>(providerEnv, ignoreCase: true, out var provider))
        {
            options.ModelProvider = provider;
        }

        var corpusRootEnv = Environment.GetEnvironmentVariable("MIGRATIONPLANNER_CORPUS_ROOT");
        if (!string.IsNullOrWhiteSpace(corpusRootEnv))
        {
            options.CorpusRoot = corpusRootEnv;
        }

        if (string.IsNullOrWhiteSpace(options.CorpusRoot))
        {
            options.CorpusRoot = ResolveDefaultCorpusRoot(builder.Environment.ContentRootPath);
        }

        var originsEnv = Environment.GetEnvironmentVariable("MIGRATIONPLANNER_ALLOWED_ORIGINS");
        if (!string.IsNullOrWhiteSpace(originsEnv))
        {
            options.AllowedOrigins = originsEnv
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        PhiModelOptions? phiOptions = null;
        if (options.ModelProvider == PlannerModelProvider.Phi)
        {
            phiOptions = new PhiModelOptions();
            builder.Configuration.GetSection(PhiModelOptions.SectionName).Bind(phiOptions);

            var endpointEnv = Environment.GetEnvironmentVariable("MIGRATIONPLANNER_PHI_ENDPOINT");
            if (!string.IsNullOrWhiteSpace(endpointEnv)) phiOptions.Endpoint = endpointEnv;

            var deploymentEnv = Environment.GetEnvironmentVariable("MIGRATIONPLANNER_PHI_DEPLOYMENT");
            if (!string.IsNullOrWhiteSpace(deploymentEnv)) phiOptions.DeploymentName = deploymentEnv;

            var keyEnv = Environment.GetEnvironmentVariable("MIGRATIONPLANNER_PHI_API_KEY");
            if (!string.IsNullOrWhiteSpace(keyEnv)) phiOptions.ApiKey = keyEnv;
        }

        HostedModelOptions? hostedOptions = null;
        if (options.ModelProvider == PlannerModelProvider.Hosted)
        {
            hostedOptions = new HostedModelOptions();
            builder.Configuration.GetSection(HostedModelOptions.SectionName).Bind(hostedOptions);

            var endpointEnv = Environment.GetEnvironmentVariable("MIGRATIONPLANNER_HOSTED_ENDPOINT");
            if (!string.IsNullOrWhiteSpace(endpointEnv)) hostedOptions.Endpoint = endpointEnv;

            var deploymentEnv = Environment.GetEnvironmentVariable("MIGRATIONPLANNER_HOSTED_DEPLOYMENT");
            if (!string.IsNullOrWhiteSpace(deploymentEnv)) hostedOptions.DeploymentName = deploymentEnv;

            var keyEnv = Environment.GetEnvironmentVariable("MIGRATIONPLANNER_HOSTED_API_KEY");
            if (!string.IsNullOrWhiteSpace(keyEnv)) hostedOptions.ApiKey = keyEnv;
        }

        builder.Services.AddSingleton(options);
        builder.Services.AddProblemDetails();

        if (options.AllowedOrigins.Length > 0)
        {
            builder.Services.AddCors(cors => cors.AddPolicy(PlannerOptions.CorsPolicyName, policy => policy
                .WithOrigins(options.AllowedOrigins)
                .WithMethods("GET", "POST", "OPTIONS")
                .WithHeaders("Content-Type")));
        }

        builder.Services.AddMigrationPlannerInfrastructure(
            corpusOptions =>
            {
                corpusOptions.CorpusRoot = options.CorpusRoot;
            },
            options.ModelProvider,
            phiOptions,
            hostedOptions);

        var app = builder.Build();

        // Force guidance-store construction so hash mismatches fail startup.
        _ = app.Services.GetRequiredService<IWindowsOnArmGuidanceStore>();

        if (options.AllowedOrigins.Length > 0)
        {
            app.UseCors(PlannerOptions.CorsPolicyName);
        }

        app.MapMigrationPlansEndpoint();
        app.MapMigrationReportsEndpoint();
        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

        app.Run();
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
