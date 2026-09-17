namespace ArmMigrationAssist.RepositoryWorkspace;

public interface IRepositoryClonePool
{
    // Returns a working copy pinned to the requested commit. Concurrent callers requesting
    // the same key share the underlying directory; the first caller performs the clone,
    // subsequent callers wait and reuse. Cleanup happens on process shutdown, not per call.
    Task<RepositoryClone> AcquireAsync(RepositoryCloneKey key, CancellationToken cancellationToken);
}

public sealed record RepositoryClone(
    string RootPath,
    RepositoryCloneKey Key,
    string ResolvedCommitSha);
