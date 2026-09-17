using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ArmMigrationAssist.RepositoryWorkspace;

public static class RepositoryWorkspaceServiceCollectionExtensions
{
    // Idempotent so multiple per-service hosts (F1/F3) can each declare their dependency on
    // the pool without collision when composed into a single WebApplication.
    public static IServiceCollection AddRepositoryClonePool(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RepositoryClonePoolOptions>()
            .Bind(configuration.GetSection(RepositoryClonePoolOptions.SectionName));
        services.TryAddSingleton<IGitProcess, GitProcess>();
        services.TryAddSingleton<IRepositoryClonePool, RepositoryClonePool>();
        services.TryAddSingleton<IWorktreeManager, WorktreeManager>();
        services.TryAddSingleton<ILocalBranchApplier, LocalBranchApplier>();
        return services;
    }
}
