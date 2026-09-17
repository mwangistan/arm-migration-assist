using Validation.BuildValidation;

namespace Validation.Api;

public interface IValidationWorkflowRunner
{
    Task<PreparedValidation> PrepareAsync(MigrationPlan migration, RepositoryTarget target, PlanningOptions options, CancellationToken cancellationToken);
    Task<ValidationReport> RunAsync(PreparedValidation prepared, PlanApproval approval, CancellationToken cancellationToken);
}

public sealed class ValidationWorkflowRunner(
    IRepositoryInspector repositoryInspector,
    IProcessRunner processRunner,
    IServiceProvider services) : IValidationWorkflowRunner
{
    public Task<PreparedValidation> PrepareAsync(MigrationPlan migration, RepositoryTarget target, PlanningOptions options, CancellationToken cancellationToken)
    {
        var workflow = CreateWorkflow();
        return workflow.PrepareAsync(migration, target, options, cancellationToken);
    }

    public Task<ValidationReport> RunAsync(PreparedValidation prepared, PlanApproval approval, CancellationToken cancellationToken)
    {
        var workflow = CreateWorkflow();
        return workflow.RunAsync(prepared, approval, cancellationToken);
    }

    private ValidationWorkflow CreateWorkflow() => new(
        repositoryInspector,
        processRunner,
        services.GetService<IValidationPlanner>(),
        services.GetService<IEvidenceAnalyzer>(),
        services.GetService<ICoverageReviewer>());
}
