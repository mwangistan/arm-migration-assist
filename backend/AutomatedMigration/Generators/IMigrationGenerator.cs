using AutomatedMigration.Models;

namespace AutomatedMigration.Generators;

// A generator turns one work item into a reviewable patch (or null if there is
// nothing to change for this repo).
public interface IMigrationGenerator
{
    GeneratedPatch? Generate(WorkItem workItem, string repoPath);
}

public sealed record GeneratedPatch(string Diff);
