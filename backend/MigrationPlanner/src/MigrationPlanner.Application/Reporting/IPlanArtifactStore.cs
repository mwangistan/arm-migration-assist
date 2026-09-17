namespace MigrationPlanner.Application.Reporting;

public interface IPlanArtifactStore
{
    void Store(MigrationReport report);

    MigrationReport? Get(string runId);
}
