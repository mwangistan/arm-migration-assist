using System.Collections.Concurrent;
using MigrationPlanner.Application.Planning;
using MigrationPlanner.Domain.Errors;

namespace MigrationPlanner.Api.Planning;

public sealed class PlanRunStore
{
    private readonly ConcurrentDictionary<string, PlanRunRecord> _records = new(StringComparer.Ordinal);

    public PlanRunRecord Create()
    {
        var record = new PlanRunRecord
        {
            RunId = "planrun-" + Guid.NewGuid().ToString("N"),
            Status = PlanRunStatus.Queued,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _records[record.RunId] = record;
        return record;
    }

    public bool TryGet(string runId, out PlanRunRecord record) => _records.TryGetValue(runId, out record!);
}

public enum PlanRunStatus { Queued, Running, Completed, Failed }

public sealed class PlanRunRecord
{
    public required string RunId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public PlanRunStatus Status { get; set; }

    // Success payload — mirrors the fields the frontend expects from the sync endpoint.
    public PlanResult? Result { get; set; }
    public object? Automation { get; set; }

    // Failure payload.
    public int? FailureStatusCode { get; set; }
    public string? FailureErrorCode { get; set; }
    public string? FailureTitle { get; set; }
    public IReadOnlyList<string>? FailureErrors { get; set; }
    public int? RetryAfterSeconds { get; set; }
}
