using System.Text.Json;
using AutomatedMigration.BuildConfiguration;
using AutomatedMigration.CodeMigration;
using AutomatedMigration.Generators;
using AutomatedMigration.Models;
using AutomatedMigration.PipelineUpdates;
using AutomatedMigration.Publishing;

// Feature 3 engine: read a migration plan, run the generators for the work items
// we own, and write reviewable patches. Nothing is applied to the target repo.
//
// Usage: dotnet run [planPath] [repoPath] [outputDir] [--publish [--push] [--remote <r>] [--branch <b>]]
var flagsWithValue = new HashSet<string>(StringComparer.Ordinal) { "--remote", "--branch" };
var positionals = new List<string>();
for (int i = 0; i < args.Length; i++)
{
    if (args[i].StartsWith("--"))
    {
        if (flagsWithValue.Contains(args[i]))
            i++; // skip the flag's value
        continue;
    }
    positionals.Add(args[i]);
}

var planPath = positionals.Count > 0 ? positionals[0] : Path.Combine("samples", "plan.sample.json");
var repoPath = positionals.Count > 1 ? positionals[1] : Path.Combine("samples", "repo");
var outputDir = positionals.Count > 2 ? positionals[2] : "output";

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

var context = new MigrationContext(
    repoPath,
    plan.WorkItems.ToDictionary(w => w.Id, StringComparer.Ordinal));

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
    var patch = generators[item.AgentOrSkill].Generate(item, context);
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

// Story 3.2 "Generate pipeline PR": optionally apply the patches to a branch as
// one commit per work item. Dry-run unless --push is given.
if (args.Contains("--publish"))
{
    var dryRun = !args.Contains("--push");
    var remote = GetOpt(args, "--remote");
    var branch = GetOpt(args, "--branch") ?? "arm64-migration";

    // Prefer Feature 1's real clone (its own git root, already has a remote).
    // Fall back to a throwaway git copy when run standalone. The "own root" check
    // avoids accidentally committing into a parent repo that merely contains the
    // input folder.
    string workRepo;
    string baseBranch;
    if (IsOwnGitRoot(repoPath))
    {
        workRepo = Path.GetFullPath(repoPath);
        baseBranch = Git.Run(workRepo, "rev-parse", "--abbrev-ref", "HEAD").Trim();
        Console.WriteLine($"\nPublishing against existing clone: {workRepo} (base: {baseBranch})");
    }
    else
    {
        workRepo = CreateGitCopy(repoPath);
        baseBranch = "main";
        Console.WriteLine($"\nNo standalone git repo at input; using throwaway copy: {workRepo}");
    }

    var result = new PrPublisher().Publish(ours, new PublishOptions(
        RepoPath: workRepo,
        OutputDir: Path.GetFullPath(outputDir),
        BranchName: branch,
        BaseBranch: baseBranch,
        DryRun: dryRun,
        Remote: remote,
        OpenPr: true));

    Console.WriteLine($"\nBranch '{result.Branch}' created with {result.Commits} commit(s).");
    Console.WriteLine(result.Pushed
        ? $"Pushed to {remote}. PR: {result.PrUrl ?? "(gh not available)"}"
        : "Dry run: commits made locally, nothing pushed.");
    Console.WriteLine("\nCommits:");
    Console.WriteLine(Git.Run(workRepo, "log", "--oneline", $"{baseBranch}..HEAD"));
    Console.WriteLine($"Workspace: {workRepo}");
}

static string? GetOpt(string[] args, string name)
{
    int i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

// True only when 'path' is the top level of its own git repo (not merely inside one).
static bool IsOwnGitRoot(string path)
{
    try
    {
        var top = Git.Run(path, "rev-parse", "--show-toplevel").Trim();
        return string.Equals(Path.GetFullPath(top), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase);
    }
    catch
    {
        return false;
    }
}

static string CreateGitCopy(string sourceRepo)
{
    var temp = Path.Combine(Path.GetTempPath(), "arm-mig-" + Guid.NewGuid().ToString("N")[..8]);
    CopyDirectory(sourceRepo, temp);
    Git.Run(temp, "init", "-q");
    Git.Run(temp, "config", "user.email", "demo@example.com");
    Git.Run(temp, "config", "user.name", "arm-migration-demo");
    Git.Run(temp, "add", "-A");
    Git.Run(temp, "commit", "-q", "-m", "baseline");
    Git.Run(temp, "branch", "-M", "main");
    return temp;
}

static void CopyDirectory(string source, string destination)
{
    Directory.CreateDirectory(destination);
    foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        Directory.CreateDirectory(dir.Replace(source, destination));
    foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        File.Copy(file, file.Replace(source, destination), overwrite: true);
}
