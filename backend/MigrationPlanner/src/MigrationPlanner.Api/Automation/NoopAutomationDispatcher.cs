using MigrationPlanner.Domain.Assessment;
using MigrationPlanner.Domain.Plan;

namespace MigrationPlanner.Api.Automation;

internal sealed class NoopAutomationDispatcher : IAutomationDispatcher
{
    public Task<AutomationDispatch?> DispatchAsync(
        MigrationPlanV1 plan,
        Repository repository,
        CancellationToken cancellationToken)
        => Task.FromResult<AutomationDispatch?>(null);
}
