using MigrationPlanner.Domain.Assessment;
using MigrationPlanner.Domain.Plan;

namespace MigrationPlanner.Api.Automation;

// Fire-and-forget bridge to F3 (AutomatedMigration.Api). F2 stays responsive:
// if F3 is unreachable or unconfigured the dispatcher returns null and the
// planner response omits the automation block instead of failing the plan.
public interface IAutomationDispatcher
{
    Task<AutomationDispatch?> DispatchAsync(MigrationPlanV1 plan, Repository repository, CancellationToken cancellationToken);
}

public sealed record AutomationDispatch(string JobId, string Status, string StatusUrl);
