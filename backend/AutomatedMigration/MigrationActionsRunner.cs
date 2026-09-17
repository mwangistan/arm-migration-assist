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

        return new MigrationRunResult(plan.PlanId, generated, skipped);
    }

    // Apply the already-generated patches to a branch and (optionally) open a PR.
    public PublishResult Publish(MigrationPlan plan, PublishOptions options) =>
        new PrPublisher().Publish(SelectWorkItems(plan), options);
}
