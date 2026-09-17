namespace MigrationPlanner.Application.Abstractions;

/// <summary>
/// Thrown when the planner model tries to invoke <see cref="IGuidanceLookup"/>
/// more times than the per-run budget allows. The orchestrator maps this to
/// <c>PlannerErrorCode.ModelToolBudgetExceeded</c>.
/// </summary>
public sealed class GuidanceLookupBudgetExceededException : Exception
{
    public GuidanceLookupBudgetExceededException(int budget)
        : base($"Guidance lookup budget of {budget} calls exhausted for this planning run.")
    {
        Budget = budget;
    }

    public int Budget { get; }
}
