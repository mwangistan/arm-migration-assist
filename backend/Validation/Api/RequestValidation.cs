using Validation.BuildValidation;

namespace Validation.Api;

public static class RequestValidation
{
    public static bool HasRequiredMigrationFields(MigrationPlan migration)
    {
        var plan = migration.ValidationPlan;
        if (string.IsNullOrWhiteSpace(migration.SchemaVersion) || string.IsNullOrWhiteSpace(migration.PlanId) ||
            plan?.TargetDevices is null || migration.WorkItems is null)
            return false;
        IReadOnlyList<ValidationCheck>?[] groups = [plan.BuildChecks, plan.FunctionalChecks,
            plan.ReliabilityChecks, plan.PerformanceChecks, plan.PowerChecks, plan.OfflineChecks,
            plan.AccessibilityChecks, plan.WindowsExperienceChecks];
        return groups.All(group => group is not null && group.All(check =>
                check is not null && !string.IsNullOrWhiteSpace(check.Id) &&
                check.Description is not null && check.ExpectedOutcome is not null)) &&
            migration.WorkItems.All(item => item is not null && !string.IsNullOrWhiteSpace(item.Id) &&
                item.AcceptanceTests is not null && item.AcceptanceTests.All(test =>
                    test is not null && !string.IsNullOrWhiteSpace(test.Id) &&
                    test.Description is not null && test.ExpectedOutcome is not null));
    }
}
