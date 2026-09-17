using Validation.BuildValidation;

namespace Validation.Dashboard;

public sealed record DashboardSummary(
    int Passed, int Failed, int NotRun, int Inconclusive, int Skipped, int Total);

public sealed record DashboardCriterion(
    string Key, CheckSource Source, string SourceId, string? WorkItemId,
    string Category, string Description, ResultStatus Status, string Reason,
    IReadOnlyList<string> EvidenceIds, IReadOnlyList<string> SourceEvidenceIds, IReadOnlyList<string> GuidanceIds);
    
public sealed record ValidationDashboard(
    string SchemaVersion, string RunId, string MigrationPlanId, string CommitSha, string? Branch,
    OverallStatus Status, DashboardSummary Summary,
    IReadOnlyList<DashboardCriterion> Checks,
    IReadOnlyList<ExecutionEvidence> DeterministicEvidence,
    IReadOnlyList<CoverageGap> CoverageGaps, AiAnalysis AiAnalysis)
{
    public static ValidationDashboard FromReport(ValidationReport report) => new(
        report.SchemaVersion, report.RunId, report.MigrationPlanId, report.Repository.CommitSha, report.Repository.Branch,
        report.Scorecard.Status,
        new(report.Scorecard.Passed, report.Scorecard.Failed, report.Scorecard.NotRun,
            report.Scorecard.Inconclusive, report.Scorecard.Skipped, report.Scorecard.Criteria.Count),
        report.Scorecard.Criteria.Select(result => new DashboardCriterion(
            result.Criterion.Key, result.Criterion.Source, result.Criterion.SourceId, result.Criterion.WorkItemId,
            result.Criterion.Category, result.Criterion.Description, result.Status, result.Reason,
            result.EvidenceIds, result.Criterion.SourceEvidenceIds, result.Criterion.GuidanceIds)).ToArray(),
        report.Evidence, report.CoverageGaps, report.Ai);

    public string ToJson() => ValidationJson.Serialize(this);
}
