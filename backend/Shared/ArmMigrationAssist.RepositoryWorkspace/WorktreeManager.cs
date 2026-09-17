using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArmMigrationAssist.RepositoryWorkspace;

public sealed class WorktreeManager : IWorktreeManager, IDisposable
{
    private readonly RepositoryClonePoolOptions _options;
    private readonly IGitProcess _git;
    private readonly ILogger<WorktreeManager> _logger;
    private readonly string _worktreeRoot;
    private readonly List<Worktree> _created = new();
    private readonly object _sync = new();
    private bool _disposed;

    public WorktreeManager(
        IOptions<RepositoryClonePoolOptions> options,
        IGitProcess git,
        IHostApplicationLifetime lifetime,
        ILogger<WorktreeManager> logger)
    {
        _options = options.Value;
        _git = git;
        _logger = logger;
        var root = string.IsNullOrWhiteSpace(_options.RootDirectory)
            ? Path.Combine(Path.GetTempPath(), "arm-migration-workspaces")
            : Path.GetFullPath(_options.RootDirectory);
        _worktreeRoot = Path.Combine(root, "worktrees");
        Directory.CreateDirectory(_worktreeRoot);
        lifetime.ApplicationStopping.Register(Dispose);
    }

    public async Task<Worktree> CreateAsync(
        RepositoryClone parent,
        string worktreeId,
        string branchName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentException.ThrowIfNullOrWhiteSpace(worktreeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(branchName);
        if (worktreeId.Any(c => !char.IsLetterOrDigit(c) && c != '-' && c != '_'))
        {
            throw new ArgumentException("worktreeId must be alphanumeric with dashes or underscores only.", nameof(worktreeId));
        }

        var worktreePath = Path.Combine(_worktreeRoot, worktreeId);
        if (Directory.Exists(worktreePath))
        {
            throw new InvalidOperationException($"Worktree '{worktreeId}' already exists at {worktreePath}.");
        }

        _logger.LogInformation("Adding worktree {Path} on branch {Branch} from {Parent}", worktreePath, branchName, parent.RootPath);

        var result = await _git.RunAsync(parent.RootPath, new[]
        {
            "worktree", "add", "-b", branchName, worktreePath, parent.ResolvedCommitSha,
        }, cancellationToken).ConfigureAwait(false);

        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"git worktree add failed ({result.ExitCode}): {Truncate(result.StdErr)}");
        }

        var worktree = new Worktree(worktreePath, branchName, parent);
        lock (_sync)
        {
            if (_disposed)
            {
                TryRemove(worktree);
                throw new ObjectDisposedException(nameof(WorktreeManager));
            }
            _created.Add(worktree);
        }
        return worktree;
    }

    public void Dispose()
    {
        List<Worktree> snapshot;
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            snapshot = _created.ToList();
            _created.Clear();
        }
        foreach (var worktree in snapshot)
        {
            TryRemove(worktree);
        }
    }

    private void TryRemove(Worktree worktree)
    {
        try
        {
            // `worktree remove --force` unregisters from the parent and deletes the directory.
            // We can't await inside Dispose, so fire a synchronous wait; the parent process is
            // shutting down anyway.
            _ = _git.RunAsync(worktree.Parent.RootPath, new[]
            {
                "worktree", "remove", "--force", worktree.Path,
            }, TimeSpan.FromSeconds(30), CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Best-effort worktree removal failed for {Path}", worktree.Path);
        }
        try
        {
            if (Directory.Exists(worktree.Path))
            {
                Directory.Delete(worktree.Path, recursive: true);
            }
        }
        catch { /* best effort */ }
    }

    private static string Truncate(string text) =>
        text.Length <= 400 ? text : text[..400] + "…";
}
