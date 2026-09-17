using System.Threading.Channels;
using Validation.Dashboard;

namespace Validation.Api;

public sealed class ValidationRunQueue
{
    private readonly Channel<string> channel;

    public ValidationRunQueue(Microsoft.Extensions.Options.IOptions<ValidationApiOptions> options)
    {
        int capacity = Math.Max(1, options.Value.QueueCapacity);
        channel = Channel.CreateBounded<string>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public ValueTask EnqueueAsync(string runId, CancellationToken cancellationToken) =>
        channel.Writer.WriteAsync(runId, cancellationToken);

    // Nonblocking: used by the request path so a full queue or a client abort can never hang a
    // request or leave a run stuck in "Queued" without anything that will ever dequeue it.
    public bool TryEnqueue(string runId) => channel.Writer.TryWrite(runId);

    public IAsyncEnumerable<string> ReadAllAsync(CancellationToken cancellationToken) =>
        channel.Reader.ReadAllAsync(cancellationToken);
}

public sealed class ValidationRunWorker(
    IValidationStore store,
    ValidationRunQueue queue,
    IValidationWorkflowRunner workflow,
    ILogger<ValidationRunWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Recovery enqueues requeued runs concurrently with the read loop below instead of
        // blocking host startup on it. If the recovered backlog exceeds the channel capacity,
        // blocking here (e.g. in StartAsync, before the reader exists) would deadlock forever:
        // the bounded writer would await capacity that only the (not-yet-running) reader could
        // ever free. Running recovery as a background task lets the reader drain the channel as
        // recovery fills it, so a backlog larger than capacity still completes.
        var recovery = RecoverAsync(stoppingToken);

        try
        {
            await foreach (string runId in queue.ReadAllAsync(stoppingToken))
            {
                try
                {
                    var record = await store.GetRunAsync(runId, stoppingToken);
                    if (record is null || record.Status != ValidationRunStatus.Queued) continue;

                    // Execute the approval/plan snapshot frozen at queue time, not whatever the plan's
                    // approval currently is. A later PUT to the plan must never change an already-queued run.
                    var snapshot = await store.GetRunSnapshotAsync(runId, stoppingToken);
                    if (snapshot is null)
                    {
                        await store.FailRunAsync(runId, "The validation run snapshot could not be loaded.", stoppingToken);
                        continue;
                    }

                    await store.MarkRunRunningAsync(runId, stoppingToken);
                    var report = await workflow.RunAsync(snapshot.Prepared, snapshot.Approval, stoppingToken);
                    report = report with { RunId = runId };
                    var dashboard = ValidationDashboard.FromReport(report);
                    await store.CompleteRunAsync(runId, report, dashboard, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Validation run {RunId} failed.", runId);
                    try
                    {
                        await store.FailRunAsync(runId, "Validation run failed unexpectedly. See server logs.", CancellationToken.None);
                    }
                    catch (Exception storeError)
                    {
                        logger.LogError(storeError, "Could not persist validation run failure for {RunId}.", runId);
                    }
                }
            }
        }
        finally
        {
            await recovery;
        }
    }

    private async Task RecoverAsync(CancellationToken stoppingToken)
    {
        try
        {
            foreach (var runId in await store.RecoverRunsAsync(stoppingToken))
                await queue.EnqueueAsync(runId, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down before recovery finished; nothing more to do.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Validation run recovery failed.");
        }
    }
}