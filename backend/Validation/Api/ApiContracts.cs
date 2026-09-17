using Validation.BuildValidation;

namespace Validation.Api;

public sealed record HealthResponse(string Status);

public sealed record CreatePlanRequest(MigrationPlan MigrationPlan, RepositoryTarget Target, PlanningOptions Options, bool IncludeProposal = false);

public sealed record UpdateApprovalRequest(IReadOnlyList<string> ApprovedCommandIds, IReadOnlyDictionary<string, string>? SkippedCommands = null, string? PlanFingerprint = null);

public sealed record PlanLinks(string Self, string Approval, string Runs);

public sealed record RunLinks(string Status, string Report, string Dashboard)
{
    public static string StatusPath(string runId) => $"/api/v1/validation/runs/{runId}";
    public static RunLinks For(string runId) => new(StatusPath(runId), $"{StatusPath(runId)}/report", $"{StatusPath(runId)}/dashboard");
}

public sealed record PlanResponse(string PlanId, string Fingerprint, string Status, DateTimeOffset CreatedAt, PlanLinks Links, PreparedValidation? Proposal = null)
{
    public static PlanResponse From(PlanMetadata metadata, PreparedValidation? includeProposal) => new(
        metadata.PlanId, metadata.Fingerprint, "prepared", metadata.CreatedAt,
        new($"/api/v1/validation/plans/{metadata.PlanId}", $"/api/v1/validation/plans/{metadata.PlanId}/approval", $"/api/v1/validation/plans/{metadata.PlanId}/runs"),
        includeProposal);
}

public sealed record ApprovalResponse(string PlanId, PlanApproval Approval)
{
    public static ApprovalResponse From(string planId, PlanApproval approval) => new(planId, approval);
}

public sealed record RunResponse(string RunId, string PlanId, ValidationRunStatus Status, DateTimeOffset CreatedAt, DateTimeOffset? StartedAt, DateTimeOffset? FinishedAt, string? Summary, string? Error, RunLinks Links)
{
    public static RunResponse From(ValidationRunRecord record) => new(record.RunId, record.PlanId, record.Status, record.CreatedAt,
        record.StartedAt, record.FinishedAt, record.Summary, record.Error, RunLinks.For(record.RunId));
}

public sealed record PlanMetadata(string PlanId, string MigrationPlanId, string Fingerprint, DateTimeOffset CreatedAt);

// Approval is null until an explicit PUT is stored; it is never auto-populated at plan creation.
public sealed record StoredPlan(PlanMetadata Metadata, PreparedValidation Prepared, PlanApproval? Approval);

public sealed record CreatedPlan(PlanMetadata Metadata, PreparedValidation Prepared);

public enum ValidationRunStatus { Queued, Running, Completed, Failed, Cancelled }

public sealed record ValidationRunRecord(
    string RunId,
    string PlanId,
    ValidationRunStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt = null,
    DateTimeOffset? FinishedAt = null,
    string? Summary = null,
    string? Error = null);
