using AutomatedMigration.Models;

namespace AutomatedMigration.Publishing;

// Story 3.2 "Generate pipeline PR": apply the generated patches to a branch as
// one commit per work item, then (optionally) push and open a PR. Dry-run by
// default so nothing leaves the machine without an explicit --push.
public sealed record PublishOptions(
    string RepoPath,
    string OutputDir,
    string BranchName,
    string BaseBranch,
    bool DryRun,
    string? Remote,
    bool OpenPr);

public sealed record PublishResult(string Branch, int Commits, bool Pushed, string? PrUrl);

public sealed class PrPublisher
{
    public PublishResult Publish(IReadOnlyList<WorkItem> workItems, PublishOptions options)
    {
        // Fails fast if the target is not a git working tree.
        Git.Run(options.RepoPath, "rev-parse", "--is-inside-work-tree");
        Git.Run(options.RepoPath, "checkout", "-b", options.BranchName);

        int commits = 0;
        foreach (var item in workItems.OrderBy(w => w.Sequence))
        {
            var patch = Path.Combine(Path.GetFullPath(options.OutputDir), $"{item.Id}.patch");
            if (!File.Exists(patch))
                continue; // generator produced no change for this work item

            Git.Run(options.RepoPath, "apply", patch);
            Git.Run(options.RepoPath, "add", "-A");
            var (subject, body) = CommitMessage(item);
            Git.Run(options.RepoPath, "commit", "-m", subject, "-m", body);
            commits++;
        }

        bool pushed = false;
        string? prUrl = null;
        if (!options.DryRun && !string.IsNullOrWhiteSpace(options.Remote))
        {
            Git.Run(options.RepoPath, "push", "-u", options.Remote, options.BranchName);
            pushed = true;
            if (options.OpenPr && Git.ToolExists("gh"))
                prUrl = Git.RunTool(options.RepoPath, "gh", "pr", "create",
                    "--base", options.BaseBranch,
                    "--head", options.BranchName,
                    "--title", $"ARM64 migration ({options.BranchName})",
                    "--body", "Automated ARM64 migration changes. Review each commit.").Trim();
        }

        return new PublishResult(options.BranchName, commits, pushed, prUrl);
    }

    // Subject = work item title; body = objective, evidence, guidance, and
    // acceptance tests, so the commit is traceable back to the plan.
    private static (string subject, string body) CommitMessage(WorkItem w)
    {
        var lines = new List<string> { w.Objective, "" };
        if (w.EvidenceIds is { Count: > 0 })
            lines.Add("Evidence: " + string.Join(", ", w.EvidenceIds));
        if (w.GuidanceIds is { Count: > 0 })
            lines.Add("Guidance: " + string.Join(", ", w.GuidanceIds));
        if (w.AcceptanceTests is { Count: > 0 })
        {
            lines.Add("Acceptance:");
            foreach (var at in w.AcceptanceTests)
                lines.Add($"  - {at.Description} => {at.ExpectedOutcome}");
        }
        lines.Add("");
        lines.Add($"Work-Item: {w.Id}");
        return (w.Title, string.Join("\n", lines));
    }
}
