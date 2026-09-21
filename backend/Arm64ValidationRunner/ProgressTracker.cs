namespace Arm64ValidationRunner;

// Live, poll-friendly progress for a single job. Each mutation rebuilds an immutable
// JobProgress snapshot and assigns it to the JobRecord so the status endpoint can read a
// consistent view without locking on the read path.
public sealed class ProgressTracker
{
    private readonly JobRecord _job;
    private readonly List<MutableStep> _steps;
    private readonly object _gate = new();

    public ProgressTracker(JobRecord job, IEnumerable<(string Key, string Label)> steps)
    {
        _job = job;
        _steps = steps.Select(s => new MutableStep(s.Key, s.Label)).ToList();
        Publish(phase: "queued");
    }

    public void Start(string key, string? detail = null) => Mutate(key, "running", detail, markStart: true);

    public void Succeed(string key, string? detail = null) => Mutate(key, "succeeded", detail, markFinish: true);

    public void Fail(string key, string detail) => Mutate(key, "failed", detail, markFinish: true);

    public void Skip(string key, string? detail = null) => Mutate(key, "skipped", detail, markFinish: true);

    public void Detail(string key, string detail) => Mutate(key, status: null, detail, markStart: false);

    public void Complete() => Publish(phase: "completed");

    private void Mutate(string key, string? status, string? detail, bool markStart = false, bool markFinish = false)
    {
        lock (_gate)
        {
            var step = _steps.FirstOrDefault(s => s.Key == key);
            if (step is null) return;
            if (status is not null) step.Status = status;
            if (detail is not null) step.Detail = detail;
            if (markStart && step.StartedAt is null) step.StartedAt = DateTimeOffset.UtcNow;
            if (markFinish) step.FinishedAt = DateTimeOffset.UtcNow;
            Publish(phase: status == "running" ? key : CurrentPhase());
        }
    }

    private string CurrentPhase()
    {
        var running = _steps.FirstOrDefault(s => s.Status == "running");
        return running?.Key ?? "queued";
    }

    private void Publish(string phase)
    {
        var snapshot = _steps
            .Select(s => new ProgressStep(s.Key, s.Label, s.Status, s.Detail, s.StartedAt, s.FinishedAt))
            .ToList();
        var terminal = _steps.Count(s => s.Status is "succeeded" or "failed" or "skipped");
        var percent = _steps.Count == 0 ? 0 : (int)Math.Round(terminal * 100.0 / _steps.Count);
        _job.Progress = new JobProgress(phase, snapshot, percent);
    }

    private sealed class MutableStep
    {
        public MutableStep(string key, string label)
        {
            Key = key;
            Label = label;
        }

        public string Key { get; }
        public string Label { get; }
        public string Status { get; set; } = "pending";
        public string? Detail { get; set; }
        public DateTimeOffset? StartedAt { get; set; }
        public DateTimeOffset? FinishedAt { get; set; }
    }
}
