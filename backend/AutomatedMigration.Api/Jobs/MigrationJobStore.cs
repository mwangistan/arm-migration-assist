using System.Collections.Concurrent;
using System.Threading.Channels;
using AutomatedMigration.Api.Configuration;
using AutomatedMigration.Api.Contracts;
using Microsoft.Extensions.Options;

namespace AutomatedMigration.Api.Jobs;

public enum MigrationJobStatus { Queued, Running, Completed, Failed }

public sealed class MigrationJob
{
    public required string JobId { get; init; }
    public required string PlanId { get; init; }
    public required RepositoryTarget Target { get; init; }
    public required AutomatedMigration.Models.MigrationPlan Plan { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public MigrationJobStatus Status { get; set; } = MigrationJobStatus.Queued;
    public MigrationActionsResult? Result { get; set; }
    public string? Error { get; set; }
}

// In-memory job store bounded by the retention options. Matches F1's pattern:
// jobs die on process restart; durable consumers should persist the result.
public sealed class MigrationJobStore
{
    private readonly ConcurrentDictionary<string, MigrationJob> _jobs = new();
    private readonly Channel<string> _queue;
    private readonly JobRetentionOptions _retention;

    public MigrationJobStore(IOptions<AutomationOptions> options)
    {
        _retention = options.Value.JobRetention;
        _queue = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
    }

    public ChannelReader<string> Queue => _queue.Reader;

    public MigrationJob Enqueue(string planId, RepositoryTarget target, AutomatedMigration.Models.MigrationPlan plan)
    {
        Trim();
        var job = new MigrationJob
        {
            JobId = "job-" + Guid.NewGuid().ToString("N"),
            PlanId = planId,
            Target = target,
            Plan = plan
        };
        _jobs[job.JobId] = job;
        _queue.Writer.TryWrite(job.JobId);
        return job;
    }

    public bool TryGet(string jobId, out MigrationJob job)
    {
        var found = _jobs.TryGetValue(jobId, out var value);
        job = value!;
        return found;
    }

    private void Trim()
    {
        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-_retention.MaxAgeMinutes);
        foreach (var (id, job) in _jobs)
        {
            if (job.CreatedAt < cutoff && job.Status is MigrationJobStatus.Completed or MigrationJobStatus.Failed)
            {
                _jobs.TryRemove(id, out _);
            }
        }
        if (_jobs.Count <= _retention.MaxJobs)
        {
            return;
        }
        foreach (var id in _jobs
                     .Where(kv => kv.Value.Status is MigrationJobStatus.Completed or MigrationJobStatus.Failed)
                     .OrderBy(kv => kv.Value.CreatedAt)
                     .Take(_jobs.Count - _retention.MaxJobs)
                     .Select(kv => kv.Key))
        {
            _jobs.TryRemove(id, out _);
        }
    }
}
