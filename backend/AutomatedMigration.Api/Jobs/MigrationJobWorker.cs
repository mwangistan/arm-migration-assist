using AutomatedMigration.Api.Configuration;
using AutomatedMigration.Api.Contracts;
using AutomatedMigration.Api.Repository;
using AutomatedMigration.CodeMigration;
using Microsoft.Extensions.Options;

namespace AutomatedMigration.Api.Jobs;

// Reads queued jobs, fetches the source at the pinned commit, runs the
// MigrationActionsRunner over an ephemeral worktree, populates the job Result,
// and cleans up. One job at a time (SingleReader=true).
public sealed class MigrationJobWorker : BackgroundService
{
    private readonly MigrationJobStore _store;
    private readonly RepositoryFetcher _fetcher;
    private readonly AutomationOptions _options;
    private readonly ILogger<MigrationJobWorker> _logger;

    public MigrationJobWorker(
        MigrationJobStore store,
        RepositoryFetcher fetcher,
        IOptions<AutomationOptions> options,
        ILogger<MigrationJobWorker> logger)
    {
        _store = store;
        _fetcher = fetcher;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var jobId in _store.Queue.ReadAllAsync(stoppingToken))
        {
            if (!_store.TryGet(jobId, out var job))
            {
                continue;
            }
            try
            {
                job.Status = MigrationJobStatus.Running;
                job.StartedAt = DateTimeOffset.UtcNow;

                using var fetched = await _fetcher.FetchAsync(job.Target.Url, job.Target.CommitSha, stoppingToken);

                var chatModel = ResolveChatModel();
                var runner = new AutomatedMigration.MigrationActionsRunner(chatModel);
                var scratchOut = Path.Combine(Path.GetTempPath(), "amma-out-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(scratchOut);
                try
                {
                    var runResult = runner.Run(job.Plan, fetched.Root, scratchOut);
                    var generated = runResult.Generated
                        .Select(g => Project(g.WorkItem, g.Diff, _options.MaxDiffBytes))
                        .ToList();
                    var skipped = runResult.Skipped
                        .Select(w => new SkippedWorkItemDto(w.Id, w.AgentOrSkill, "no change produced"))
                        .ToList();

                    job.Result = new MigrationActionsResult(
                        job.PlanId,
                        job.Target.CommitSha,
                        generated,
                        skipped);
                    job.Status = MigrationJobStatus.Completed;
                }
                finally
                {
                    RepositoryFetcher.TryDeleteDirectory(scratchOut);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                job.Status = MigrationJobStatus.Failed;
                job.Error = "Cancelled during shutdown.";
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Migration job {JobId} failed", job.JobId);
                job.Status = MigrationJobStatus.Failed;
                job.Error = ex.Message;
            }
            finally
            {
                job.FinishedAt = DateTimeOffset.UtcNow;
            }
        }
    }

    private static IChatModel? ResolveChatModel()
    {
        var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        return string.IsNullOrWhiteSpace(token) ? null : new GitHubModelsChatModel(token);
    }

    private static GeneratedPatchDto Project(
        AutomatedMigration.Models.WorkItem workItem, string diff, int maxBytes)
    {
        var bytes = System.Text.Encoding.UTF8.GetByteCount(diff);
        var truncated = bytes > maxBytes;
        var payload = truncated ? Truncate(diff, maxBytes) : diff;
        return new GeneratedPatchDto(
            workItem.Id,
            workItem.AgentOrSkill,
            workItem.Title,
            payload,
            bytes,
            truncated,
            workItem.EvidenceIds ?? Array.Empty<string>(),
            workItem.AcceptanceTests ?? Array.Empty<AutomatedMigration.Models.AcceptanceTest>());
    }

    private static string Truncate(string diff, int maxBytes)
    {
        var utf8 = System.Text.Encoding.UTF8.GetBytes(diff);
        if (utf8.Length <= maxBytes)
        {
            return diff;
        }
        // Trim to the last complete newline within the cap so consumers get a valid partial diff.
        var slice = utf8.AsSpan(0, maxBytes);
        var lastNewline = slice.LastIndexOf((byte)'\n');
        var end = lastNewline > 0 ? lastNewline + 1 : maxBytes;
        return System.Text.Encoding.UTF8.GetString(utf8, 0, end);
    }
}
