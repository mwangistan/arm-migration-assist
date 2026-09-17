using Microsoft.Extensions.Logging;

namespace ArmMigrationAssist.RepositoryWorkspace;

public sealed class LocalBranchApplier : ILocalBranchApplier
{
    private readonly IGitProcess _git;
    private readonly ILogger<LocalBranchApplier> _logger;

    public LocalBranchApplier(IGitProcess git, ILogger<LocalBranchApplier> logger)
    {
        _git = git;
        _logger = logger;
    }

    public async Task<BranchApplication> ApplyAsync(
        Worktree worktree,
        IReadOnlyList<PatchInput> patches,
        string commitMessage,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(worktree);
        ArgumentNullException.ThrowIfNull(patches);
        ArgumentException.ThrowIfNullOrWhiteSpace(commitMessage);

        var applied = new List<string>();
        var rejected = new List<PatchRejection>();

        // Patch files staged under the worktree so cleanup happens automatically when the
        // worktree is removed; never under the parent clone.
        var patchDir = Path.Combine(worktree.Path, ".arm-migration", "patches");
        Directory.CreateDirectory(patchDir);

        foreach (var patch in patches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(patch.DiffContent))
            {
                rejected.Add(new PatchRejection(patch.Id, "empty diff"));
                continue;
            }

            var patchFile = Path.Combine(patchDir, SanitizeFileName(patch.Id) + ".patch");
            await File.WriteAllTextAsync(patchFile, patch.DiffContent, cancellationToken).ConfigureAwait(false);

            // `--allow-empty` is intentionally NOT passed: it makes git accept malformed input
            // as a "commit-message-only" patch, which would misclassify garbage as applied.
            var check = await _git.RunAsync(worktree.Path, new[]
            {
                "apply", "--check", patchFile,
            }, cancellationToken).ConfigureAwait(false);

            if (check.Succeeded)
            {
                var apply = await _git.RunAsync(worktree.Path, new[]
                {
                    "apply", patchFile,
                }, cancellationToken).ConfigureAwait(false);
                if (apply.Succeeded)
                {
                    applied.Add(patch.Id);
                    continue;
                }
                rejected.Add(new PatchRejection(patch.Id, $"git apply failed: {Truncate(apply.StdErr)}"));
                continue;
            }

            // Strict --check failed. Try 3-way merge as a fallback for context drift; the
            // parent commit's tree provides the fallback merge base.
            var threeway = await _git.RunAsync(worktree.Path, new[]
            {
                "apply", "--3way", patchFile,
            }, cancellationToken).ConfigureAwait(false);
            if (threeway.Succeeded)
            {
                applied.Add(patch.Id);
                _logger.LogInformation("Patch {Id} applied via 3-way merge (context drift)", patch.Id);
                continue;
            }

            rejected.Add(new PatchRejection(patch.Id, $"strict + 3-way apply failed: {Truncate(threeway.StdErr)}"));
        }

        if (applied.Count == 0)
        {
            var currentHead = await ReadHeadAsync(worktree.Path, cancellationToken).ConfigureAwait(false);
            return new BranchApplication(worktree.BranchName, currentHead, applied, rejected, CommitCreated: false);
        }

        // Stage everything (new files, modifications, deletions) touched by the applied patches.
        var addAll = await _git.RunAsync(worktree.Path, new[] { "add", "-A" }, cancellationToken).ConfigureAwait(false);
        if (!addAll.Succeeded)
        {
            throw new InvalidOperationException($"git add -A failed: {Truncate(addAll.StdErr)}");
        }

        var commit = await _git.RunAsync(worktree.Path, new[]
        {
            "-c", "user.name=arm-migration-assist",
            "-c", "user.email=arm-migration-assist@users.noreply.github.com",
            "commit", "-m", commitMessage,
        }, cancellationToken).ConfigureAwait(false);
        if (!commit.Succeeded)
        {
            throw new InvalidOperationException($"git commit failed: {Truncate(commit.StdErr)}");
        }

        var head = await ReadHeadAsync(worktree.Path, cancellationToken).ConfigureAwait(false);
        return new BranchApplication(worktree.BranchName, head, applied, rejected, CommitCreated: true);
    }

    private async Task<string> ReadHeadAsync(string worktreePath, CancellationToken cancellationToken)
    {
        var revParse = await _git.RunAsync(worktreePath, new[] { "rev-parse", "HEAD" }, cancellationToken).ConfigureAwait(false);
        return revParse.Succeeded ? revParse.StdOut.Trim().ToLowerInvariant() : string.Empty;
    }

    private static string SanitizeFileName(string id)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = id.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        return new string(chars);
    }

    private static string Truncate(string text) =>
        text.Length <= 400 ? text : text[..400] + "…";
}
