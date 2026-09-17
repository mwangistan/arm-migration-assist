using MigrationPlanner.Application.Reporting;

namespace MigrationPlanner.Infrastructure.Reporting;

internal sealed class MemoryPlanArtifactStore : IPlanArtifactStore
{
    private const int MaximumReports = 100;
    private readonly object sync = new();
    private readonly Dictionary<string, MigrationReport> reports = new(StringComparer.Ordinal);
    private readonly Queue<string> insertionOrder = new();

    public void Store(MigrationReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        lock (sync)
        {
            if (!reports.ContainsKey(report.RunId))
            {
                insertionOrder.Enqueue(report.RunId);
            }

            reports[report.RunId] = report;
            while (reports.Count > MaximumReports)
            {
                reports.Remove(insertionOrder.Dequeue());
            }
        }
    }

    public MigrationReport? Get(string runId)
    {
        lock (sync)
        {
            return reports.GetValueOrDefault(runId);
        }
    }
}
