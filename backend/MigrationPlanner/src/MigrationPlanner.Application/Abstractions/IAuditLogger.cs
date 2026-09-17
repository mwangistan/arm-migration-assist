using MigrationPlanner.Application.Auditing;

namespace MigrationPlanner.Application.Abstractions;

/// <summary>
/// Structured audit sink. Implementations must never persist secrets or raw
/// source content.
/// </summary>
public interface IAuditLogger
{
    void Record(AuditEvent auditEvent);
}
