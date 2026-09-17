using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using ArmMigrationAssist.RepositoryDiscovery.Models;

namespace ArmMigrationAssist.RepositoryDiscovery.Jobs;

public sealed record AssessmentProgress(
    string Phase,
    int Percent,
    string Message);

public sealed record AssessmentJobEvent(
    long Sequence,
    string EventType,
    string Phase,
    int Percent,
    string Message,
    DateTimeOffset Timestamp);

public sealed record AssessmentJobResource(
    string JobId,
    string Status,
    string Phase,
    int Percent,
    string Message,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? ErrorCode,
    string? Error,
    string StatusUrl,
    string EventsUrl,
    string ResultUrl);

internal sealed class AssessmentExecutionGate
{
    private readonly SemaphoreSlim semaphore = new(2);

    public bool TryEnter() => semaphore.Wait(0);

    public Task EnterAsync(CancellationToken cancellationToken) =>
        semaphore.WaitAsync(cancellationToken);

    public void Exit() => semaphore.Release();
}

internal sealed class AssessmentJobQueue
{
    private const int MaximumRetainedJobs = 100;
    private static readonly TimeSpan Retention = TimeSpan.FromHours(1);
    private readonly Channel<AssessmentJobState> pending = Channel.CreateBounded<AssessmentJobState>(
        new BoundedChannelOptions(32)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = false,
            SingleReader = false,
        });
    private readonly ConcurrentDictionary<string, AssessmentJobState> jobs = new(StringComparer.Ordinal);

    public AssessmentJobState? Enqueue(string source, string? authenticationSessionId)
    {
        RemoveExpiredJobs();
        RemoveOldestTerminalJobsAtCapacity();
        if (jobs.Count >= MaximumRetainedJobs)
        {
            return null;
        }

        var state = new AssessmentJobState(source, authenticationSessionId);
        if (!jobs.TryAdd(state.JobId, state))
        {
            return null;
        }

        if (!pending.Writer.TryWrite(state))
        {
            jobs.TryRemove(state.JobId, out _);
            return null;
        }

        return state;
    }

    public AssessmentJobState? Get(string jobId)
    {
        RemoveExpiredJobs();
        return IsValidJobId(jobId) && jobs.TryGetValue(jobId, out var state) ? state : null;
    }

    public IAsyncEnumerable<AssessmentJobState> ReadAllAsync(CancellationToken cancellationToken) =>
        pending.Reader.ReadAllAsync(cancellationToken);

    private void RemoveExpiredJobs()
    {
        var cutoff = DateTimeOffset.UtcNow.Subtract(Retention);
        foreach (var expired in jobs
                     .Where(item => item.Value.IsTerminal && item.Value.UpdatedAt < cutoff)
                     .Select(item => item.Key))
        {
            jobs.TryRemove(expired, out _);
        }
    }

    private void RemoveOldestTerminalJobsAtCapacity()
    {
        var removalCount = jobs.Count - MaximumRetainedJobs + 1;
        if (removalCount <= 0)
        {
            return;
        }

        foreach (var completed in jobs.Values
                     .Where(job => job.IsTerminal)
                     .OrderBy(job => job.UpdatedAt)
                     .Take(removalCount))
        {
            jobs.TryRemove(completed.JobId, out _);
        }
    }

    private static bool IsValidJobId(string jobId) =>
        jobId.Length == 32 && jobId.All(Uri.IsHexDigit);
}

internal sealed class AssessmentJobState
{
    private readonly object stateLock = new();
    private readonly List<AssessmentJobEvent> events = [];
    private readonly CancellationTokenSource cancellation = new();
    private TaskCompletionSource<bool> eventSignal = CreateEventSignal();
    private long sequence;
    private string status = "queued";
    private string phase = "queued";
    private int percent;
    private string message = "Assessment queued.";
    private DateTimeOffset updatedAt;
    private string? errorCode;
    private string? error;
    private RepositoryAssessment? result;

    public AssessmentJobState(string source, string? authenticationSessionId)
    {
        JobId = Guid.NewGuid().ToString("N");
        Source = source;
        AuthenticationSessionId = authenticationSessionId;
        CreatedAt = DateTimeOffset.UtcNow;
        updatedAt = CreatedAt;
        AddEvent("queued", phase, percent, message);
    }

    public string JobId { get; }
    public string Source { get; }
    public string? AuthenticationSessionId { get; }
    public bool UseStoredCredentials => AuthenticationSessionId is not null;
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get { lock (stateLock) return updatedAt; } }
    public bool IsTerminal { get { lock (stateLock) return status is "completed" or "failed" or "canceled"; } }
    public CancellationToken CancellationToken => cancellation.Token;

    public AssessmentJobResource Snapshot()
    {
        lock (stateLock)
        {
            var baseUrl = $"/api/assessment-jobs/{JobId}";
            return new AssessmentJobResource(
                JobId,
                status,
                phase,
                percent,
                message,
                CreatedAt,
                updatedAt,
                errorCode,
                error,
                baseUrl,
                $"{baseUrl}/events",
                $"{baseUrl}/result");
        }
    }

    public RepositoryAssessment? GetResult()
    {
        lock (stateLock)
        {
            return result;
        }
    }

    public IReadOnlyList<AssessmentJobEvent> GetEventsAfter(long afterSequence)
    {
        lock (stateLock)
        {
            return events.Where(item => item.Sequence > afterSequence).ToArray();
        }
    }

    public Task WaitForEventAsync(long afterSequence, CancellationToken cancellationToken)
    {
        lock (stateLock)
        {
            if (sequence > afterSequence || status is "completed" or "failed" or "canceled")
            {
                return Task.CompletedTask;
            }

            return eventSignal.Task.WaitAsync(cancellationToken);
        }
    }

    public void MarkRunning()
    {
        Update("running", "starting", 1, "Assessment started.", "started");
    }

    public void Report(AssessmentProgress progress)
    {
        Update(
            "running",
            progress.Phase,
            Math.Clamp(progress.Percent, 1, 99),
            progress.Message,
            "progress");
    }

    public void Complete(RepositoryAssessment assessment)
    {
        lock (stateLock)
        {
            if (IsTerminalStatus(status))
            {
                return;
            }

            result = assessment;
            SetStateLocked("completed", "completed", 100, "Assessment completed.", "completed");
        }
    }

    public void Fail(string code, string failure)
    {
        lock (stateLock)
        {
            if (IsTerminalStatus(status))
            {
                return;
            }

            errorCode = code;
            error = failure;
            SetStateLocked("failed", "failed", percent, failure, "failed");
        }
    }

    public void Cancel()
    {
        lock (stateLock)
        {
            if (IsTerminalStatus(status))
            {
                return;
            }

            SetStateLocked("canceled", "canceled", percent, "Assessment canceled.", "canceled");
        }

        cancellation.Cancel();
    }

    private void Update(
        string nextStatus,
        string nextPhase,
        int nextPercent,
        string nextMessage,
        string eventType)
    {
        lock (stateLock)
        {
            if (IsTerminalStatus(status))
            {
                return;
            }

            SetStateLocked(nextStatus, nextPhase, nextPercent, nextMessage, eventType);
        }
    }

    private void SetStateLocked(
        string nextStatus,
        string nextPhase,
        int nextPercent,
        string nextMessage,
        string eventType)
    {
        status = nextStatus;
        phase = nextPhase;
        percent = nextPercent;
        message = nextMessage;
        updatedAt = DateTimeOffset.UtcNow;
        AddEventLocked(eventType, phase, percent, message);
    }

    private void AddEvent(string eventType, string eventPhase, int eventPercent, string eventMessage)
    {
        lock (stateLock)
        {
            AddEventLocked(eventType, eventPhase, eventPercent, eventMessage);
        }
    }

    private void AddEventLocked(string eventType, string eventPhase, int eventPercent, string eventMessage)
    {
        events.Add(new AssessmentJobEvent(
            ++sequence,
            eventType,
            eventPhase,
            eventPercent,
            eventMessage,
            DateTimeOffset.UtcNow));
        var previousSignal = eventSignal;
        eventSignal = CreateEventSignal();
        previousSignal.TrySetResult(true);
    }

    private static TaskCompletionSource<bool> CreateEventSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static bool IsTerminalStatus(string value) =>
        value is "completed" or "failed" or "canceled";
}

internal sealed class AssessmentJobWorker(
    AssessmentJobQueue queue,
    AssessmentExecutionGate executionGate,
    IRepositoryAssessmentService assessmentService,
    Authentication.IGitHubAuthenticationBroker authenticationBroker) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.WhenAll(
                ProcessJobsAsync(stoppingToken),
                ProcessJobsAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutdown is not a job failure.
        }
    }

    private async Task ProcessJobsAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in queue.ReadAllAsync(stoppingToken))
        {
            if (job.CancellationToken.IsCancellationRequested)
            {
                continue;
            }

            var enteredGate = false;
            try
            {
                using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                    stoppingToken,
                    job.CancellationToken);
                await executionGate.EnterAsync(linkedCancellation.Token);
                enteredGate = true;
                job.MarkRunning();
                var progress = new InlineProgress(job.Report);
                var assessment = await assessmentService.DiscoverAsync(
                    job.Source,
                    linkedCancellation.Token,
                    new RepositoryAccessOptions(job.UseStoredCredentials),
                    progress);
                job.Complete(assessment);
            }
            catch (OperationCanceledException) when (job.CancellationToken.IsCancellationRequested)
            {
                job.Cancel();
            }
            catch (RepositoryAuthenticationRequiredException)
            {
                if (job.AuthenticationSessionId is null)
                {
                    job.Fail("authentication-required", "GitHub authentication is required for this repository.");
                }
                else
                {
                    authenticationBroker.Invalidate(job.AuthenticationSessionId);
                    job.Fail(
                        "access-denied",
                        "The signed-in GitHub account cannot access this repository. Verify organization SSO access, then try again.");
                }
            }
            catch (RepositoryDiscoveryException exception)
            {
                job.Fail("assessment-failed", exception.Message);
            }
            catch (Exception) when (!stoppingToken.IsCancellationRequested)
            {
                job.Fail("assessment-failed", "The repository assessment failed unexpectedly.");
            }
            finally
            {
                if (enteredGate)
                {
                    executionGate.Exit();
                }
            }
        }
    }

    private sealed class InlineProgress(Action<AssessmentProgress> report) : IProgress<AssessmentProgress>
    {
        public void Report(AssessmentProgress value) => report(value);
    }
}

internal static class AssessmentJobEvents
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task StreamAsync(
        AssessmentJobState job,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.Append("X-Accel-Buffering", "no");
        await context.Response.WriteAsync("retry: 3000\n\n", cancellationToken);

        var lastSequence = long.TryParse(context.Request.Headers["Last-Event-ID"], out var parsed)
            ? Math.Max(0, parsed)
            : 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            foreach (var item in job.GetEventsAfter(lastSequence))
            {
                await context.Response.WriteAsync($"id: {item.Sequence}\n", cancellationToken);
                await context.Response.WriteAsync("event: assessment\n", cancellationToken);
                await context.Response.WriteAsync(
                    $"data: {JsonSerializer.Serialize(item, JsonOptions)}\n\n",
                    cancellationToken);
                lastSequence = item.Sequence;
            }

            await context.Response.Body.FlushAsync(cancellationToken);
            if (job.IsTerminal)
            {
                return;
            }

            using var heartbeat = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            heartbeat.CancelAfter(TimeSpan.FromSeconds(15));
            try
            {
                await job.WaitForEventAsync(lastSequence, heartbeat.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                await context.Response.WriteAsync(": keep-alive\n\n", cancellationToken);
                await context.Response.Body.FlushAsync(cancellationToken);
            }
        }
    }
}