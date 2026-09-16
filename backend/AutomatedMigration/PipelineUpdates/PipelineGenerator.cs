using AutomatedMigration.Generators;
using AutomatedMigration.Models;

namespace AutomatedMigration.PipelineUpdates;

// Story 3.2 — adds an ARM64 build job as a new CI workflow.
public sealed class PipelineGenerator : IMigrationGenerator
{
    public GeneratedPatch? Generate(WorkItem workItem, string repoPath)
    {
        const string path = ".github/workflows/arm64-build.yml";
        const string workflow =
            """
            name: arm64-build
            on:
              push:
              pull_request:
            jobs:
              build-arm64:
                runs-on: ubuntu-24.04-arm
                steps:
                  - uses: actions/checkout@v4
                  - name: Build for linux/arm64
                    run: docker buildx build --platform linux/arm64 -t app:arm64 .
            """;

        var diff = UnifiedDiff.NewFile(path, workflow);
        return new GeneratedPatch(diff);
    }
}
