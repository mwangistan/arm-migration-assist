namespace ArmMigrationAssist.RepositoryWorkspace;

// An isolated working copy created via `git worktree add` off a RepositoryClone. Shares the
// parent's object store (no extra fetch) but has its own HEAD, index, and working tree so
// concurrent F3 jobs on the same commit don't step on each other's checkouts.
public sealed record Worktree(
    string Path,
    string BranchName,
    RepositoryClone Parent);

public interface IWorktreeManager
{
    // Creates `Path` as a fresh worktree of `parent.RootPath` on a new branch `branchName`
    // starting at parent's HEAD. Fails loudly if the path already exists.
    Task<Worktree> CreateAsync(RepositoryClone parent, string worktreeId, string branchName, CancellationToken cancellationToken);
}
