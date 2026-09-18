using ArmMigrationAssist.RepositoryWorkspace;
using AutomatedMigration.Api.Configuration;
using AutomatedMigration.Api.Contracts;
using AutomatedMigration.Api.Validation;
using AutomatedMigration.CodeMigration;
using Microsoft.Extensions.Options;

namespace AutomatedMigration.Api.Jobs;

public sealed class MigrationJobWorker : BackgroundService
{
    private readonly MigrationJobStore _store;
    private readonly IRepositoryClonePool _clonePool;
    private readonly IWorktreeManager _worktreeManager;
    private readonly ILocalBranchApplier _branchApplier;
    private readonly IValidationDispatcher _validationDispatcher;
    private readonly IArm64BuildDispatcher _arm64Dispatcher;
    private readonly AutomationOptions _options;
    private readonly ILogger<MigrationJobWorker> _logger;

    public MigrationJobWorker(
        MigrationJobStore store,
        IRepositoryClonePool clonePool,
        IWorktreeManager worktreeManager,
        ILocalBranchApplier branchApplier,
        IValidationDispatcher validationDispatcher,
        IArm64BuildDispatcher arm64Dispatcher,
        IOptions<AutomationOptions> options,
        ILogger<MigrationJobWorker> logger)
    {
        _store = store;
        _clonePool = clonePool;
        _worktreeManager = worktreeManager;
        _branchApplier = branchApplier;
        _validationDispatcher = validationDispatcher;
        _arm64Dispatcher = arm64Dispatcher;
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

                var cloneKey = new RepositoryCloneKey(job.Target.Url, job.Target.CommitSha, Anonymous: true);
                var clone = await _clonePool.AcquireAsync(cloneKey, stoppingToken);

                var worktreeId = SanitizeWorktreeId(job.JobId);
                var branchName = $"arm-migration/{worktreeId}";
                var worktree = await _worktreeManager.CreateAsync(clone, worktreeId, branchName, stoppingToken);

                var chatModel = ResolveChatModel();
                var runner = new AutomatedMigration.MigrationActionsRunner(chatModel);
                var scratchOut = Path.Combine(Path.GetTempPath(), "amma-out-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(scratchOut);
                try
                {
                    var runResult = runner.Run(job.Plan, worktree.Path, scratchOut);
                    var generatedDtos = runResult.Generated
                        .Select(g => Project(g.WorkItem, g.Diff, _options.MaxDiffBytes))
                        .ToList();
                    var skippedDtos = runResult.Skipped
                        .Select(w => new SkippedWorkItemDto(w.Id, w.AgentOrSkill, "no change produced"))
                        .ToList();

                    var patches = runResult.Generated
                        .Select(g => new PatchInput(g.WorkItem.Id, g.Diff))
                        .ToList();

                    var application = await _branchApplier.ApplyAsync(
                        worktree,
                        patches,
                        $"arm-migration: {job.Plan.PlanId}",
                        stoppingToken);

                    var branchDto = new BranchApplicationDto(
                        worktree.Path,
                        application.BranchName,
                        application.BranchHeadSha,
                        application.CommitCreated,
                        application.AppliedIds,
                        application.RejectedIds
                            .Select(r => new PatchRejectionDto(r.Id, r.Reason))
                            .ToList());

                    ValidationDispatchDto? validationDto = null;
                    Arm64BuildDispatchDto? arm64Dto = null;
                    if (application.CommitCreated)
                    {
                        validationDto = await _validationDispatcher.DispatchAsync(
                            job.Plan,
                            worktree.Path,
                            application.BranchName,
                            application.BranchHeadSha,
                            stoppingToken);

                        var arm64Patches = runResult.Generated
                            .Where(g => application.AppliedIds.Contains(g.WorkItem.Id))
                            .Select(g => new Arm64PatchInput(g.WorkItem.Id, g.Diff))
                            .ToList();
                        if (arm64Patches.Count > 0)
                        {
                            arm64Dto = await _arm64Dispatcher.DispatchAsync(
                                job.Plan,
                                job.Target.Url,
                                clone.ResolvedCommitSha,
                                arm64Patches,
                                stoppingToken);
                        }
                    }

                    job.Result = new MigrationActionsResult(
                        job.PlanId,
                        clone.ResolvedCommitSha,
                        generatedDtos,
                        skippedDtos,
                        branchDto,
                        validationDto,
                        arm64Dto);
                    job.Status = MigrationJobStatus.Completed;
                }
                finally
                {
                    TryDeleteDirectory(scratchOut);
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

    private static string SanitizeWorktreeId(string jobId)
    {
        var chars = jobId.Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_').ToArray();
        return new string(chars);
    }

    private static IChatModel? ResolveChatModel()
    {
        var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        return string.IsNullOrWhiteSpace(token) ? null : new GitHubModelsChatModel(token);
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
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
        if (utf8.Length <= maxBytes) return diff;
        var slice = utf8.AsSpan(0, maxBytes);
        var lastNewline = slice.LastIndexOf((byte)'\n');
        var end = lastNewline > 0 ? lastNewline + 1 : maxBytes;
        return System.Text.Encoding.UTF8.GetString(utf8, 0, end);
    }
}