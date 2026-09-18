using System.Collections.Concurrent;

namespace Arm64ValidationRunner;

public sealed class JobRecord
{
    public required string JobId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public string Status { get; set; } = "queued";
    public Arm64Scorecard? Scorecard { get; set; }
    public string? Error { get; set; }
}

public sealed class JobStore
{
    private readonly ConcurrentDictionary<string, JobRecord> _records = new();

    public JobRecord Create()
    {
        var id = Guid.NewGuid().ToString("N");
        var record = new JobRecord { JobId = id, CreatedAt = DateTimeOffset.UtcNow };
        _records[id] = record;
        return record;
    }

    public bool TryGet(string id, out JobRecord record) => _records.TryGetValue(id, out record!);
}

public sealed class RunnerOptions
{
    public string BearerToken { get; init; } = string.Empty;
    public string VmSku { get; init; } = "Standard_D4ps_v6";
    public string Region { get; init; } = "eastus2";
    public string WorkRoot { get; init; } = "/var/lib/arm-validation-runner/work";
    public string PublicBaseUrl { get; init; } = string.Empty;
}
