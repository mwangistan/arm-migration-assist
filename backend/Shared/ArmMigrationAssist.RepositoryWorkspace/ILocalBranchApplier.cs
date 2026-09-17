namespace ArmMigrationAssist.RepositoryWorkspace;

public sealed record PatchInput(string Id, string DiffContent);

public sealed record BranchApplication(
    string BranchName,
    string BranchHeadSha,
    IReadOnlyList<string> AppliedIds,
    IReadOnlyList<PatchRejection> RejectedIds,
    bool CommitCreated);

public sealed record PatchRejection(string Id, string Reason);

public interface ILocalBranchApplier
{
    // Applies patches to the worktree's tip commit and (if any applied) commits them on the
    // worktree's active branch. Best-effort: rejects are returned rather than throwing.
    Task<BranchApplication> ApplyAsync(
        Worktree worktree,
        IReadOnlyList<PatchInput> patches,
        string commitMessage,
        CancellationToken cancellationToken);
}
