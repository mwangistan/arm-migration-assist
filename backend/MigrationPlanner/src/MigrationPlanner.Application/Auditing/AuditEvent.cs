namespace MigrationPlanner.Application.Auditing;

/// <summary>
/// Structured audit event emitted for every planning run. Never carries raw
/// source content or secrets — only IDs, versions, and outcome codes.
/// </summary>
public sealed record AuditEvent(
    string RunId,
    string AssessmentId,
    string CommitSha,
    string SchemaVersion,
    string ModelProvider,
    string ModelName,
    string CorpusVersion,
    IReadOnlyCollection<string> McpToolsInvoked,
    IReadOnlyCollection<string> EvidenceIdsAccessed,
    IReadOnlyCollection<string> GuidanceIdsRetrieved,
    string ValidationResult,
    string? ErrorCode = null,
    DateTimeOffset? OccurredAt = null);
