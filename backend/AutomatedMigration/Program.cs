using System.Text.Json;
using AutomatedMigration;
using AutomatedMigration.CodeMigration;
using AutomatedMigration.Models;
using AutomatedMigration.Publishing;

// Thin CLI over MigrationActionsRunner (Feature 3's callable entry point).
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

// The code transformer uses GitHub Models when GITHUB_TOKEN is set; otherwise it skips.
var githubToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
IChatModel? chatModel = string.IsNullOrWhiteSpace(githubToken) ? null : new GitHubModelsChatModel(githubToken);

var json = File.ReadAllText(planPath);
var plan = JsonSerializer.Deserialize<MigrationPlan>(json, new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,
}) ?? throw new InvalidOperationException($"Could not parse migration plan: {planPath}");

var runner = new MigrationActionsRunner(chatModel);
var result = runner.Run(plan, repoPath, outputDir);

Console.WriteLine($"Plan {plan.PlanId} (recommended path: {plan.RecommendedPath})");
Console.WriteLine($"Generated {result.Generated.Count} patch(es); {result.Skipped.Count} produced no change.\n");
foreach (var g in result.Generated)
    Console.WriteLine($"[{g.WorkItem.Sequence}] {g.WorkItem.Id} -> {g.WorkItem.AgentOrSkill}: wrote {g.PatchPath}");

Console.WriteLine("\nDone. Patches are for review only - nothing was applied.");

// Story 3.2 "Generate pipeline PR": optionally apply the patches to a branch as
// one commit per work item. Dry-run unless --push is given.
if (args.Contains("--publish"))
{
    if (result.Generated.Count == 0)
    {
        Console.WriteLine("Nothing to publish.");
        return;
    }

    var dryRun = !args.Contains("--push");
    var remote = GetOpt(args, "--remote");
    var branch = GetOpt(args, "--branch") ?? $"arm64-migration/{plan.PlanId}";

    // Feature 1 provides the clone (its own git root, with a remote). Required.
    if (!IsOwnGitRoot(repoPath))
    {
        Console.Error.WriteLine($"--publish requires a git clone at '{repoPath}' (provided by Feature 1). Aborting.");
        return;
    }

    var workRepo = Path.GetFullPath(repoPath);
    var baseBranch = Git.Run(workRepo, "rev-parse", "--abbrev-ref", "HEAD").Trim();
    Console.WriteLine($"\nPublishing against clone: {workRepo} (base: {baseBranch})");

    var publishResult = runner.Publish(plan, new PublishOptions(
        RepoPath: workRepo,
        OutputDir: Path.GetFullPath(outputDir),
        BranchName: branch,
        BaseBranch: baseBranch,
        DryRun: dryRun,
        Remote: remote,
        OpenPr: true));

    Console.WriteLine($"\nBranch '{publishResult.Branch}' created with {publishResult.Commits} commit(s).");
    Console.WriteLine(publishResult.Pushed
        ? $"Pushed to {remote}. PR: {publishResult.PrUrl ?? "(gh not available)"}"
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
