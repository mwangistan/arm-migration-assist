using Microsoft.Extensions.Logging;
using MigrationPlanner.Application.Abstractions;
using MigrationPlanner.Application.Auditing;

namespace MigrationPlanner.Infrastructure.Auditing;

/// <summary>
/// Emits structured audit events to <see cref="ILogger{T}"/>. Never logs
/// secrets or raw source content — only IDs, versions, and outcome codes.
/// </summary>
public sealed class LoggerAuditLogger : IAuditLogger
{
    private static readonly EventId AuditEventId = new(1000, "MigrationPlannerRun");

    private readonly ILogger<LoggerAuditLogger> _logger;

    public LoggerAuditLogger(ILogger<LoggerAuditLogger> logger)
    {
        _logger = logger;
    }

    public void Record(AuditEvent auditEvent)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
#pragma warning disable CA2254 // Message template is constant; structured field bag exceeds LoggerMessage arity.
        _logger.Log(
            LogLevel.Information,
            AuditEventId,
            "planner_run runId={RunId} assessmentId={AssessmentId} commitSha={CommitSha} schemaVersion={SchemaVersion} provider={ModelProvider} model={ModelName} corpusVersion={CorpusVersion} mcpTools={McpTools} evidenceIds={EvidenceIds} guidanceIds={GuidanceIds} result={Result} errorCode={ErrorCode}",
            auditEvent.RunId,
            auditEvent.AssessmentId,
            auditEvent.CommitSha,
            auditEvent.SchemaVersion,
            auditEvent.ModelProvider,
            auditEvent.ModelName,
            auditEvent.CorpusVersion,
            string.Join(",", auditEvent.McpToolsInvoked),
            string.Join(",", auditEvent.EvidenceIdsAccessed),
            string.Join(",", auditEvent.GuidanceIdsRetrieved),
            auditEvent.ValidationResult,
            auditEvent.ErrorCode);
#pragma warning restore CA2254
    }
}
