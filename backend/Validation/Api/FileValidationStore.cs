using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.RegularExpressions;
using Validation.BuildValidation;
using Validation.Dashboard;

namespace Validation.Api;

public interface IValidationStore
{
    bool StorageRootIsInsideTarget(string targetRepositoryPath);
    bool EvidenceDirectoryIsInsideStorage(string evidenceDirectory);
    Task<CreatedPlan> CreatePlanAsync(PreparedValidation prepared, CancellationToken cancellationToken);
    Task<StoredPlan?> GetPlanAsync(string planId, CancellationToken cancellationToken);
    Task SaveApprovalAsync(string planId, PlanApproval approval, CancellationToken cancellationToken);
    Task<ValidationRunRecord?> CreateRunAsync(string planId, PreparedValidation prepared, PlanApproval approval, CancellationToken cancellationToken);
    Task<ValidationRunRecord?> GetRunAsync(string runId, CancellationToken cancellationToken);
    Task<RunSnapshot?> GetRunSnapshotAsync(string runId, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> RecoverRunsAsync(CancellationToken cancellationToken);
    Task MarkRunRunningAsync(string runId, CancellationToken cancellationToken);
    Task CompleteRunAsync(string runId, ValidationReport report, ValidationDashboard dashboard, CancellationToken cancellationToken);
    Task FailRunAsync(string runId, string error, CancellationToken cancellationToken);
    Task<ValidationReport?> GetReportAsync(string runId, CancellationToken cancellationToken);
    Task<ValidationDashboard?> GetDashboardAsync(string runId, CancellationToken cancellationToken);
}

// Approval and prepared plan data are frozen into the run directory at queue time so a
// later PUT to the plan's approval (or, in principle, a later plan edit) can never change
// what an already-queued run executes.
public sealed record RunSnapshot(PreparedValidation Prepared, PlanApproval Approval);

// Small, dependency-free "is B inside A" helper shared by every containment check
// (storage-root-vs-target, evidence-dir-vs-target, evidence-dir-vs-storage-root).
public static class PathGuard
{
    public static bool IsSameOrInside(string basePath, string candidatePath)
    {
        string b = Path.TrimEndingDirectorySeparator(Path.GetFullPath(basePath));
        string c = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidatePath));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        string prefix = Path.EndsInDirectorySeparator(b) ? b : b + Path.DirectorySeparatorChar;
        return c.Equals(b, comparison) || c.StartsWith(prefix, comparison);
    }
}

public sealed class FileValidationStore : IValidationStore
{
    private static readonly Regex SafeId = new(@"\A[a-f0-9]{32}\z", RegexOptions.CultureInvariant);
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string root;
    private readonly ILogger<FileValidationStore> logger;

    public FileValidationStore(IOptions<ValidationApiOptions> options, IHostEnvironment environment, ILogger<FileValidationStore>? logger = null)
    {
        this.logger = logger ?? NullLogger<FileValidationStore>.Instance;
        if (!string.IsNullOrWhiteSpace(options.Value.StorageRoot) && !Path.IsPathFullyQualified(options.Value.StorageRoot))
            throw new InvalidOperationException("ValidationApi:StorageRoot must be an absolute path.");
        root = string.IsNullOrWhiteSpace(options.Value.StorageRoot)
            ? Path.Combine(environment.ContentRootPath, "artifacts", "validation-api")
            : Path.GetFullPath(options.Value.StorageRoot);
        RepositoryPaths.RejectLinks(root);
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(PlansRoot);
        Directory.CreateDirectory(RunsRoot);
    }

    private string PlansRoot => Path.Combine(root, "plans");
    private string RunsRoot => Path.Combine(root, "runs");

    public bool StorageRootIsInsideTarget(string targetRepositoryPath) => PathGuard.IsSameOrInside(targetRepositoryPath, root);

    public bool EvidenceDirectoryIsInsideStorage(string evidenceDirectory) => PathGuard.IsSameOrInside(root, evidenceDirectory);

    public async Task<CreatedPlan> CreatePlanAsync(PreparedValidation prepared, CancellationToken cancellationToken)
    {
        var planId = NewId();
        var metadata = new PlanMetadata(planId, prepared.MigrationPlanId, PlanSafety.Fingerprint(prepared), DateTimeOffset.UtcNow);
        await gate.WaitAsync(cancellationToken);
        try
        {
            string directory = PlanDirectory(planId);
            Directory.CreateDirectory(directory);
            await WriteJsonAsync(Path.Combine(directory, "metadata.json"), metadata, cancellationToken);
            await WriteJsonAsync(Path.Combine(directory, "proposal.json"), prepared, cancellationToken);
            // Deliberately no approval.json here: no approval is stored until an explicit PUT.
            // GET /approval synthesizes a display-only skeleton; POST /runs treats "no file" as
            // "not yet approved" and returns 409 rather than silently running nothing.
        }
        finally
        {
            gate.Release();
        }
        return new(metadata, prepared);
    }

    public async Task<StoredPlan?> GetPlanAsync(string planId, CancellationToken cancellationToken)
    {
        if (!IsSafeId(planId)) return null;
        await gate.WaitAsync(cancellationToken);
        try
        {
            string directory = PlanDirectory(planId);
            string metadataPath = Path.Combine(directory, "metadata.json");
            if (!File.Exists(metadataPath)) return null;
            var metadata = await ReadJsonAsync<PlanMetadata>(metadataPath, cancellationToken);
            if (metadata.PlanId != planId || string.IsNullOrWhiteSpace(metadata.Fingerprint) ||
                string.IsNullOrWhiteSpace(metadata.MigrationPlanId))
                throw new InvalidDataException("Stored plan metadata is invalid.");
            string approvalPath = Path.Combine(directory, "approval.json");
            PlanApproval? approval = File.Exists(approvalPath)
                ? await ReadJsonAsync<PlanApproval>(approvalPath, cancellationToken)
                : null;
            return new(
                metadata,
                await ReadJsonAsync<PreparedValidation>(Path.Combine(directory, "proposal.json"), cancellationToken),
                approval);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task SaveApprovalAsync(string planId, PlanApproval approval, CancellationToken cancellationToken)
    {
        RequireSafeId(planId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            string directory = PlanDirectory(planId);
            if (!File.Exists(Path.Combine(directory, "metadata.json")))
                throw new FileNotFoundException("Validation plan was not found.", directory);
            await WriteJsonAsync(Path.Combine(directory, "approval.json"), approval, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<ValidationRunRecord?> CreateRunAsync(string planId, PreparedValidation prepared, PlanApproval approval, CancellationToken cancellationToken)
    {
        RequireSafeId(planId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            string reservation = Path.Combine(PlanDirectory(planId), "run-reservation.json");
            // Proof paths are plan-scoped. Reserve the plan permanently, including on failure,
            // so sequential runs and quarantined records cannot reuse stale proof files.
            if (File.Exists(reservation) || await HasRunForPlanAsync(planId, cancellationToken))
                return null;

            var run = new ValidationRunRecord(NewId(), planId, ValidationRunStatus.Queued, DateTimeOffset.UtcNow);
            // Once admitted, finish persistence independently of request abort. Publish run.json
            // last: recovery must never see a queued record with incomplete snapshots.
            cancellationToken = CancellationToken.None;
            await WriteJsonAsync(reservation, run.RunId, cancellationToken);
            string directory = RunDirectory(run.RunId);
            Directory.CreateDirectory(directory);
            Directory.CreateDirectory(Path.Combine(directory, "outputs"));
            // Freeze exactly what this run will execute. A later PUT to the plan's approval
            // (or a hypothetical future plan edit) must never retroactively change a queued run.
            await WriteJsonAsync(Path.Combine(directory, "plan-snapshot.json"), prepared, cancellationToken);
            await WriteJsonAsync(Path.Combine(directory, "approval-snapshot.json"), approval, cancellationToken);
            await WriteJsonAsync(Path.Combine(directory, "run.json"), run, cancellationToken);
            return run;
        }
        finally
        {
            gate.Release();
        }
    }

    // Caller must already hold `gate`.
    private async Task<bool> HasRunForPlanAsync(string planId, CancellationToken cancellationToken)
    {
        foreach (var directory in Directory.EnumerateDirectories(RunsRoot))
        {
            string runId = Path.GetFileName(directory);
            if (!IsSafeId(runId)) continue;
            string path = Path.Combine(directory, "run.json");
            if (!File.Exists(path)) continue;
            ValidationRunRecord record;
            try
            {
                record = await ReadJsonAsync<ValidationRunRecord>(path, cancellationToken);
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException)
            {
                logger.LogWarning(ex, "Ignoring unreadable validation run record {RunId} while checking for an active run.", runId);
                continue;
            }
            if (record.PlanId == planId)
                return true;
        }
        return false;
    }

    public async Task<ValidationRunRecord?> GetRunAsync(string runId, CancellationToken cancellationToken)
    {
        if (!IsSafeId(runId)) return null;
        await gate.WaitAsync(cancellationToken);
        try
        {
            string path = Path.Combine(RunDirectory(runId), "run.json");
            return File.Exists(path) ? await ReadJsonAsync<ValidationRunRecord>(path, cancellationToken) : null;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<RunSnapshot?> GetRunSnapshotAsync(string runId, CancellationToken cancellationToken)
    {
        if (!IsSafeId(runId)) return null;
        await gate.WaitAsync(cancellationToken);
        try
        {
            string directory = RunDirectory(runId);
            string preparedPath = Path.Combine(directory, "plan-snapshot.json");
            string approvalPath = Path.Combine(directory, "approval-snapshot.json");
            if (!File.Exists(preparedPath) || !File.Exists(approvalPath)) return null;
            return new(
                await ReadJsonAsync<PreparedValidation>(preparedPath, cancellationToken),
                await ReadJsonAsync<PlanApproval>(approvalPath, cancellationToken));
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<string>> RecoverRunsAsync(CancellationToken cancellationToken)
    {
        var queued = new List<string>();
        await gate.WaitAsync(cancellationToken);
        try
        {
            foreach (var directory in Directory.EnumerateDirectories(RunsRoot))
            {
                string runId = Path.GetFileName(directory);
                if (!IsSafeId(runId)) continue;
                string path = Path.Combine(directory, "run.json");
                if (!File.Exists(path)) continue;

                // One malformed/corrupt run.json must never abort recovery for every other run:
                // isolate the failure to this record, quarantine the file, and keep going.
                ValidationRunRecord record;
                try
                {
                    record = await ReadJsonAsync<ValidationRunRecord>(path, cancellationToken);
                    if (record.RunId != runId || !IsSafeId(record.PlanId) || !Enum.IsDefined(record.Status))
                        throw new InvalidDataException("Invalid validation run record.");
                }
                catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException or UnauthorizedAccessException)
                {
                    logger.LogError(ex, "Skipping unreadable validation run record {RunId} during recovery.", runId);
                    QuarantineUnreadable(path);
                    continue;
                }

                if (record.Status == ValidationRunStatus.Queued)
                    queued.Add(record.RunId);
                else if (record.Status == ValidationRunStatus.Running)
                {
                    try
                    {
                        await WriteJsonAsync(path, record with
                        {
                            Status = ValidationRunStatus.Failed,
                            FinishedAt = DateTimeOffset.UtcNow,
                            Error = "Run was interrupted by API shutdown before completion."
                        }, cancellationToken);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        logger.LogError(ex, "Could not mark interrupted validation run {RunId} as failed during recovery.", runId);
                    }
                }
            }
        }
        finally
        {
            gate.Release();
        }
        return queued;
    }

    private void QuarantineUnreadable(string path)
    {
        try
        {
            string quarantined = $"{path}.corrupt-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}";
            File.Move(path, quarantined);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not quarantine unreadable validation run record at {Path}.", path);
        }
    }

    public Task MarkRunRunningAsync(string runId, CancellationToken cancellationToken) =>
        UpdateRunAsync(runId, record => record with { Status = ValidationRunStatus.Running, StartedAt = DateTimeOffset.UtcNow }, cancellationToken);

    public async Task CompleteRunAsync(string runId, ValidationReport report, ValidationDashboard dashboard, CancellationToken cancellationToken)
    {
        RequireSafeId(runId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            string directory = RunDirectory(runId);
            await WriteJsonAsync(Path.Combine(directory, "outputs", "report.json"), report, cancellationToken);
            await WriteJsonAsync(Path.Combine(directory, "outputs", "dashboard.json"), dashboard, cancellationToken);
            var record = await ReadJsonAsync<ValidationRunRecord>(Path.Combine(directory, "run.json"), cancellationToken);
            var completed = record with
            {
                Status = ValidationRunStatus.Completed,
                FinishedAt = DateTimeOffset.UtcNow,
                Summary = $"{report.Scorecard.Status}: {report.Scorecard.Passed} passed, {report.Scorecard.Failed} failed, {report.Scorecard.NotRun} not run."
            };
            await WriteJsonAsync(Path.Combine(directory, "run.json"), completed, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public Task FailRunAsync(string runId, string error, CancellationToken cancellationToken) =>
        UpdateRunAsync(runId, record => record with
        {
            Status = ValidationRunStatus.Failed,
            FinishedAt = DateTimeOffset.UtcNow,
            Error = error
        }, cancellationToken);

    public Task<ValidationReport?> GetReportAsync(string runId, CancellationToken cancellationToken) =>
        ReadOutputAsync<ValidationReport>(runId, "report.json", cancellationToken);

    public Task<ValidationDashboard?> GetDashboardAsync(string runId, CancellationToken cancellationToken) =>
        ReadOutputAsync<ValidationDashboard>(runId, "dashboard.json", cancellationToken);

    private async Task<T?> ReadOutputAsync<T>(string runId, string fileName, CancellationToken cancellationToken)
    {
        if (!IsSafeId(runId)) return default;
        await gate.WaitAsync(cancellationToken);
        try
        {
            string path = Path.Combine(RunDirectory(runId), "outputs", fileName);
            return File.Exists(path) ? await ReadJsonAsync<T>(path, cancellationToken) : default;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task UpdateRunAsync(string runId, Func<ValidationRunRecord, ValidationRunRecord> update, CancellationToken cancellationToken)
    {
        RequireSafeId(runId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            string path = Path.Combine(RunDirectory(runId), "run.json");
            var record = await ReadJsonAsync<ValidationRunRecord>(path, cancellationToken);
            await WriteJsonAsync(path, update(record), cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private string PlanDirectory(string planId) => SafeCombine(PlansRoot, planId);
    private string RunDirectory(string runId) => SafeCombine(RunsRoot, runId);

    private static string SafeCombine(string parent, string id)
    {
        RequireSafeId(id);
        string path = Path.GetFullPath(Path.Combine(parent, id));
        string fullParent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!path.StartsWith(fullParent + Path.DirectorySeparatorChar, comparison))
            throw new InvalidDataException("Storage path escapes validation API root.");
        RepositoryPaths.RejectLinks(path, fullParent);
        return path;
    }

    private static async Task WriteJsonAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = Path.Combine(Path.GetDirectoryName(path)!, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(temp, ValidationJson.Serialize(value), cancellationToken);
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    File.Move(temp, path, overwrite: true);
                    return;
                }
                catch (IOException) when (attempt < 4)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(20 * (attempt + 1)), cancellationToken);
                }
            }
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    private static async Task<T> ReadJsonAsync<T>(string path, CancellationToken cancellationToken) =>
        ValidationJson.Deserialize<T>(await File.ReadAllTextAsync(path, cancellationToken));

    private static string NewId() => Guid.NewGuid().ToString("N");
    private static bool IsSafeId(string? value) => value is not null && SafeId.IsMatch(value);
    private static void RequireSafeId(string value)
    {
        if (!IsSafeId(value)) throw new InvalidDataException("Invalid validation API identifier.");
    }

}