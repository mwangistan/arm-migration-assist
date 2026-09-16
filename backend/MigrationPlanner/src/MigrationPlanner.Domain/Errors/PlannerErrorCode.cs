namespace MigrationPlanner.Domain.Errors;

/// <summary>
/// Stable Problem Details error codes surfaced by the migration planner API.
/// These strings appear in the `code` extension of RFC 9457 responses and must
/// remain stable across releases.
/// </summary>
public static class PlannerErrorCode
{
    public const string SchemaInvalid = "planner.schema.invalid";
    public const string SchemaUnsupportedVersion = "planner.schema.unsupportedVersion";
    public const string EvidenceInvalid = "planner.evidence.invalid";
    public const string EvidenceDuplicateId = "planner.evidence.duplicateId";
    public const string ScannerCoverageInvalid = "planner.scanCoverage.invalid";
    public const string McpToolUnknown = "planner.mcp.unknownTool";
    public const string CorpusIntegrity = "planner.corpus.integrity";
    public const string ModelFailed = "planner.model.failed";
    public const string ModelOutputInvalid = "planner.model.invalidOutput";
    public const string ModelToolViolation = "planner.model.toolViolation";
    public const string ModelToolBudgetExceeded = "planner.model.toolBudgetExceeded";
    public const string PlanSafetyViolation = "planner.plan.unsafe";
    public const string PlanShapeInvalid = "planner.plan.shapeInvalid";
    public const string PlanScoreDigestMismatch = "planner.plan.scoreDigestMismatch";
    public const string PlanRecommendationInconsistent = "planner.plan.recommendationInconsistent";
    public const string PlanEvidenceMissing = "planner.plan.missingEvidence";
    public const string PlanGuidanceMissing = "planner.plan.missingGuidance";
    public const string Internal = "planner.internal";
}
