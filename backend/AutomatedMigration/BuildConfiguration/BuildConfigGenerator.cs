using AutomatedMigration.Generators;
using AutomatedMigration.Models;

namespace AutomatedMigration.BuildConfiguration;

// Story 3.1 — adds an ARM64 build target to the repo's build/packaging config.
// MVP: ensures the Dockerfile base image builds for linux/arm64.
public sealed class BuildConfigGenerator : IMigrationGenerator
{
    public GeneratedPatch? Generate(WorkItem workItem, string repoPath)
    {
        var dockerfile = Path.Combine(repoPath, "Dockerfile");
        if (!File.Exists(dockerfile))
            return null;

        var lines = File.ReadAllLines(dockerfile).ToList();
        int fromIndex = lines.FindIndex(l =>
            l.TrimStart().StartsWith("FROM ", StringComparison.OrdinalIgnoreCase));
        if (fromIndex < 0)
            return null;

        var original = lines[fromIndex];
        if (original.Contains("--platform=", StringComparison.OrdinalIgnoreCase))
            return null; // already pinned to a platform

        var indent = original[..(original.Length - original.TrimStart().Length)];
        var afterFrom = original.TrimStart()["FROM ".Length..];
        var newLine = $"{indent}FROM --platform=linux/arm64 {afterFrom}";

        var diff = UnifiedDiff.ReplaceLine("Dockerfile", lines, fromIndex, newLine);
        return new GeneratedPatch(diff);
    }
}
