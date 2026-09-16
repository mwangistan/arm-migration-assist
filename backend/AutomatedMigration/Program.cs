using System.Text.Json;
using AutomatedMigration.BuildConfiguration;
using AutomatedMigration.CodeMigration;
using AutomatedMigration.Generators;
using AutomatedMigration.Models;
using AutomatedMigration.PipelineUpdates;

// Feature 3 engine: read a migration plan, run the generators for the work items
// we own, and write reviewable patches. Nothing is applied to the target repo.
//
// Usage: dotnet run [planPath] [repoPath] [outputDir]
var planPath = args.Length > 0 ? args[0] : Path.Combine("samples", "plan.sample.json");
var repoPath = args.Length > 1 ? args[1] : Path.Combine("samples", "repo");
var outputDir = args.Length > 2 ? args[2] : "output";

// Skill name -> generator. A work item is "ours" only if its agentOrSkill is a key here.
var generators = new Dictionary<string, IMigrationGenerator>(StringComparer.Ordinal)
{
    ["build-config-generator"] = new BuildConfigGenerator(), // Story 3.1
    ["ci-pipeline-generator"] = new PipelineGenerator(),     // Story 3.2
    ["code-transformer"] = new CodePatcher(),                // Story 3.3
};

var json = File.ReadAllText(planPath);
var plan = JsonSerializer.Deserialize<MigrationPlan>(json, new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,
}) ?? throw new InvalidOperationException($"Could not parse migration plan: {planPath}");

var ours = plan.WorkItems
    .Where(w => generators.ContainsKey(w.AgentOrSkill))
    .OrderBy(w => w.Sequence)
    .ToList();

if (ours.Count == 0)
{
    Console.WriteLine("No migration-action work items in this plan.");
    return;
}

Directory.CreateDirectory(outputDir);
Console.WriteLine($"Plan {plan.PlanId} (recommended path: {plan.RecommendedPath})");
Console.WriteLine($"Found {ours.Count} work item(s) for Feature 3.\n");

foreach (var item in ours)
{
    Console.WriteLine($"[{item.Sequence}] {item.Id} -> {item.AgentOrSkill}: {item.Title}");
    var patch = generators[item.AgentOrSkill].Generate(item, repoPath);
    if (patch is null)
    {
        Console.WriteLine("    no change produced\n");
        continue;
    }

    var patchPath = Path.Combine(outputDir, $"{item.Id}.patch");
    File.WriteAllText(patchPath, patch.Diff);
    Console.WriteLine($"    wrote {patchPath}\n");
}

Console.WriteLine("Done. Patches are for review only - nothing was applied.");
