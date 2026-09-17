using AutomatedMigration.Models;

namespace AutomatedMigration.Generators;

// A generator turns one work item into a reviewable patch (or null if there is
// nothing to change for this repo).
public interface IMigrationGenerator
{
    GeneratedPatch? Generate(WorkItem workItem, MigrationContext context);
}

public sealed record GeneratedPatch(string Diff);

// Everything a generator may need beyond the single work item: the repo on disk
// and a lookup of all work items (so dependencies can be resolved).
public sealed record MigrationContext(string RepoPath, IReadOnlyDictionary<string, WorkItem> WorkItemsById);
