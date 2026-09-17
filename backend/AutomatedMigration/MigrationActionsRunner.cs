using System.Text.Json;
using AutomatedMigration.BuildConfiguration;
using AutomatedMigration.CodeMigration;
using AutomatedMigration.Generators;
using AutomatedMigration.Models;
using AutomatedMigration.PipelineUpdates;
using AutomatedMigration.Publishing;

namespace AutomatedMigration;

// Feature 3's entry point. The planner (or an orchestrator) calls Run() with a
// deserialized MigrationPlan and the clone path to get reviewable patches, then
// optionally Publish() to put them on a branch / open a PR.
public sealed record GeneratedItem(WorkItem WorkItem, string PatchPath, string Diff);

public sealed record MigrationRunResult(
    string PlanId,
    IReadOnlyList<GeneratedItem> Generated,
    IReadOnlyList<WorkItem> Skipped);

public sealed class MigrationActionsRunner
{
    private readonly IReadOnlyDictionary<string, IMigrationGenerator> _generators;

    public MigrationActionsRunner(IChatModel? chatModel = null)
    {
        _generators = new Dictionary<string, IMigrationGenerator>(StringComparer.Ordinal)
        {
            ["build-config-generator"] = new BuildConfigGenerator(), // Story 3.1
            ["ci-pipeline-generator"] = new PipelineGenerator(),     // Story 3.2
            ["code-transformer"] = new CodePatcher(chatModel),       // Story 3.3
        };
    }

    // The work items this feature owns, in execution order.
    public IReadOnlyList<WorkItem> SelectWorkItems(MigrationPlan plan) =>
        plan.WorkItems
            .Where(w => _generators.ContainsKey(w.AgentOrSkill))
            .OrderBy(w => w.Sequence)
            .ToList();

    // Generate patches for the plan's work items and write them to outputDir.
    public MigrationRunResult Run(MigrationPlan plan, string repoPath, string outputDir)
    {
        var context = new MigrationContext(
            repoPath,
            plan.WorkItems.ToDictionary(w => w.Id, StringComparer.Ordinal));

        Directory.CreateDirectory(outputDir);

        var generated = new List<GeneratedItem>();
        var skipped = new List<WorkItem>();

        foreach (var item in SelectWorkItems(plan))
        {
            var patch = _generators[item.AgentOrSkill].Generate(item, context);
            if (patch is null)
            {
                skipped.Add(item);
                continue;
            }

            var patchPath = Path.Combine(outputDir, $"{item.Id}.patch");
            File.WriteAllText(patchPath, patch.Diff);
            generated.Add(new GeneratedItem(item, patchPath, patch.Diff));
        }

        var result = new MigrationRunResult(plan.PlanId, generated, skipped);
        WriteArtifact(outputDir, ToArtifact(result, branch: null, baseBranch: null));
        return result;
    }

    // Apply the already-generated patches to a branch and (optionally) open a PR.
    public PublishResult Publish(MigrationPlan plan, PublishOptions options)
    {
        var result = new PrPublisher().Publish(SelectWorkItems(plan), options);
        if (result.Commits > 0)
            StampBranch(options.OutputDir, options.BranchName, options.BaseBranch);
        return result;
    }

    private const string ResultFileName = "migration-result.json";
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private static MigrationResultArtifact ToArtifact(MigrationRunResult result, string? branch, string? baseBranch) =>
        new(
            "1.0",
            result.PlanId,
            branch,
            baseBranch,
            result.Generated.Select(g => new GeneratedChange(
                g.WorkItem.Id,
                g.WorkItem.AgentOrSkill,
                g.WorkItem.Title,
                g.PatchPath.Replace('\\', '/'),
                g.WorkItem.EvidenceIds ?? new List<string>(),
                g.WorkItem.AcceptanceTests ?? new List<AcceptanceTest>())).ToList(),
            result.Skipped.Select(w => new SkippedChange(w.Id, "no change produced")).ToList());

    private static void WriteArtifact(string outputDir, MigrationResultArtifact artifact) =>
        File.WriteAllText(Path.Combine(outputDir, ResultFileName), JsonSerializer.Serialize(artifact, JsonOpts));

    private static void StampBranch(string outputDir, string branch, string baseBranch)
    {
        var path = Path.Combine(outputDir, ResultFileName);
        if (!File.Exists(path))
            return;
        var existing = JsonSerializer.Deserialize<MigrationResultArtifact>(File.ReadAllText(path), JsonOpts);
        if (existing is null)
            return;
        File.WriteAllText(path, JsonSerializer.Serialize(existing with { Branch = branch, BaseBranch = baseBranch }, JsonOpts));
    }
}

// Feature 3 -> Feature 4 hand-off artifact (written to output/migration-result.json).
public sealed record MigrationResultArtifact(
    string SchemaVersion,
    string PlanId,
    string? Branch,
    string? BaseBranch,
    IReadOnlyList<GeneratedChange> Generated,
    IReadOnlyList<SkippedChange> Skipped);

public sealed record GeneratedChange(
    string WorkItemId,
    string AgentOrSkill,
    string Title,
    string PatchPath,
    IReadOnlyList<string> EvidenceIds,
    IReadOnlyList<AcceptanceTest> AcceptanceTests);

public sealed record SkippedChange(string WorkItemId, string Reason);
