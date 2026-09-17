using MigrationPlanner.Application.Planning;
using MigrationPlanner.Domain.Assessment;

namespace MigrationPlanner.Application.Reporting;

public static class MigrationReportFactory
{
    public static MigrationReport From(RepositoryAssessmentV1 assessment, PlanResult result)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        ArgumentNullException.ThrowIfNull(result);

        if (!result.IsSuccess || result.Plan is null || result.Score is null)
        {
            throw new InvalidOperationException(
                "MigrationReport can only be produced from a successful PlanResult with plan and score.");
        }

        var repo = new RepositoryIdentity(
            assessment.Repository.Name,
            assessment.Repository.Url,
            assessment.Repository.CommitSha,
            assessment.Repository.DefaultBranch,
            assessment.Repository.License);

        return new MigrationReport(
            result.RunId,
            assessment.AssessmentId,
            repo,
            result.Plan,
            result.Score,
            result.Warnings);
    }
}
