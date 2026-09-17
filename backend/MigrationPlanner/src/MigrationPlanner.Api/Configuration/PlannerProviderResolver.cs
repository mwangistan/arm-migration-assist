using MigrationPlanner.Infrastructure.DependencyInjection;

namespace MigrationPlanner.Api.Configuration;

/// Resolves the planner model provider from env vars or configuration and
/// refuses to fall back silently. A missing selector throws, so a
/// misconfigured production deploy fails at startup instead of serving canned
/// Fake plans to real callers.
public static class PlannerProviderResolver
{
    public const string EnvVarName = "MIGRATIONPLANNER_MODEL_PROVIDER";

    public static PlannerModelProvider Resolve(
        IConfiguration configuration,
        Func<string, string?>? environmentVariableAccessor = null)
    {
        environmentVariableAccessor ??= Environment.GetEnvironmentVariable;

        var providerEnv = environmentVariableAccessor(EnvVarName);
        if (!string.IsNullOrWhiteSpace(providerEnv))
        {
            if (Enum.TryParse<PlannerModelProvider>(providerEnv, ignoreCase: true, out var envProvider))
            {
                return envProvider;
            }

            throw new InvalidOperationException(
                $"{EnvVarName}='{providerEnv}' is not a recognized provider. " +
                $"Accepted values: {string.Join(", ", Enum.GetNames<PlannerModelProvider>())}.");
        }

        var configValue = configuration.GetSection(PlannerOptions.SectionName)["ModelProvider"];
        if (!string.IsNullOrWhiteSpace(configValue))
        {
            if (Enum.TryParse<PlannerModelProvider>(configValue, ignoreCase: true, out var configProvider))
            {
                return configProvider;
            }

            throw new InvalidOperationException(
                $"{PlannerOptions.SectionName}:ModelProvider='{configValue}' is not a recognized provider. " +
                $"Accepted values: {string.Join(", ", Enum.GetNames<PlannerModelProvider>())}.");
        }

        throw new InvalidOperationException(
            $"Migration planner refuses to start without an explicit model provider. " +
            $"Set '{EnvVarName}' or '{PlannerOptions.SectionName}:ModelProvider' to one of: " +
            $"{string.Join(", ", Enum.GetNames<PlannerModelProvider>())}. " +
            "Fake must be requested explicitly - silent fallback is disabled by design. " +
            "See backend/MigrationPlanner/RUN_LOCAL.md for the local-dev + deploy recipes.");
    }
}
