using Microsoft.Extensions.DependencyInjection;
using MigrationPlanner.Application.Abstractions;
using MigrationPlanner.Application.Planning;
using MigrationPlanner.Infrastructure.Auditing;
using MigrationPlanner.Infrastructure.Guidance;
using MigrationPlanner.Infrastructure.Mcp;
using MigrationPlanner.Infrastructure.Mcp.Tools;
using MigrationPlanner.Infrastructure.Model;
using MigrationPlanner.Infrastructure.Schema;
using MigrationPlanner.Infrastructure.Scoring;
using MigrationPlanner.Infrastructure.Validation;

namespace MigrationPlanner.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers every infrastructure service required by the planner.
    /// Callers must configure <see cref="EmbeddedGuidanceStoreOptions.CorpusRoot"/>
    /// before requesting <see cref="IWindowsOnArmGuidanceStore"/>.
    /// </summary>
    public static IServiceCollection AddMigrationPlannerInfrastructure(
        this IServiceCollection services,
        Action<EmbeddedGuidanceStoreOptions> configureCorpus,
        PlannerModelProvider modelProvider,
        PhiModelOptions? phiOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureCorpus);

        var corpusOptions = new EmbeddedGuidanceStoreOptions();
        configureCorpus(corpusOptions);

        services.AddSingleton(corpusOptions);
        services.AddSingleton<IWindowsOnArmGuidanceStore>(_ => new EmbeddedGuidanceStore(corpusOptions));

        services.AddSingleton(new GuidanceLookupOptions());
        services.AddSingleton<IGuidanceLookupFactory>(sp => new BoundedGuidanceLookupFactory(
            sp.GetRequiredService<IWindowsOnArmGuidanceStore>(),
            sp.GetRequiredService<GuidanceLookupOptions>()));

        services.AddSingleton<IAssessmentSchemaValidator, AssessmentSchemaValidator>();
        services.AddSingleton<IPlanSchemaValidator, PlanSchemaValidator>();
        services.AddSingleton<IEvidenceValidator, EvidenceValidator>();
        services.AddSingleton<IReadinessScorer, DeterministicReadinessScorer>();
        services.AddSingleton<IPlanSafetyValidator, PlanSafetyValidator>();
        services.AddSingleton<IAuditLogger, LoggerAuditLogger>();

        switch (modelProvider)
        {
            case PlannerModelProvider.Fake:
                services.AddSingleton<IPlannerModel, FakePlannerModel>();
                break;
            case PlannerModelProvider.Hosted:
                throw new NotSupportedException(
                    "Hosted (Azure OpenAI / tool-calling) provider is not implemented in this skeleton.");
            case PlannerModelProvider.Phi:
                if (phiOptions is null)
                {
                    throw new InvalidOperationException(
                        "PhiModelOptions must be supplied when PlannerModelProvider.Phi is selected.");
                }
                services.AddSingleton(phiOptions);
                services.AddSingleton<IPlannerModel, PhiPlannerModel>();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(modelProvider), modelProvider, null);
        }

        services.AddSingleton<IMcpTool, GetRepositorySummaryTool>();
        services.AddSingleton<IMcpTool, GetDependencyFindingsTool>();
        services.AddSingleton<IMcpTool, GetCodeCompatibilityFindingsTool>();
        services.AddSingleton<IMcpTool, GetBuildReadinessFindingsTool>();
        services.AddSingleton<IMcpTool, GetWindowsExperienceFindingsTool>();
        services.AddSingleton<IMcpTool, GetScanCoverageTool>();
        services.AddSingleton<IMcpTool, GetAvailableSkillCatalogTool>();
        services.AddSingleton<IMcpTool, CalculateReadinessTool>();
        services.AddSingleton<IMcpTool, LookupWindowsArmGuidanceTool>();
        services.AddSingleton<IMcpToolRegistry, MockMcpToolRegistry>();

        services.AddSingleton<MigrationPlanningService>();

        return services;
    }
}

public enum PlannerModelProvider
{
    Fake,
    Hosted,
    Phi,
}
