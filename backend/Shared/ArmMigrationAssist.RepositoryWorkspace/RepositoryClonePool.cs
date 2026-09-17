using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArmMigrationAssist.RepositoryWorkspace;

// Process-lifetime cache of git working copies keyed by (RepositoryUrl, CommitSha, Anonymous).
// Concurrent callers requesting the same key share the underlying directory; the first caller
// clones, subsequent callers wait on the same materialization task. Cleanup happens on process
// shutdown so consecutive F1→F3 requests within a demo run reuse the same clone.
public sealed class RepositoryClonePool : IRepositoryClonePool, IDisposable
{
    private readonly RepositoryClonePoolOptions _options;
    private readonly IGitProcess _git;
    private readonly ILogger<RepositoryClonePool> _logger;
    private readonly string _rootDirectory;
    private readonly Dictionary<RepositoryCloneKey, Task<RepositoryClone>> _materializations = new();
    private readonly object _sync = new();
    private bool _disposed;

    public RepositoryClonePool(
        IOptions<RepositoryClonePoolOptions> options,
        IGitProcess git,
        IHostApplicationLifetime lifetime,
        ILogger<RepositoryClonePool> logger)
    {
        _options = options.Value;
        _git = git;
        _logger = logger;
        _rootDirectory = string.IsNullOrWhiteSpace(_options.RootDirectory)
            ? Path.Combine(Path.GetTempPath(), "arm-migration-workspaces")
            : Path.GetFullPath(_options.RootDirectory);
        Directory.CreateDirectory(_rootDirectory);
        lifetime.ApplicationStopping.Register(Dispose);
    }

    public Task<RepositoryClone> AcquireAsync(RepositoryCloneKey key, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        ValidateKey(key);

        Task<RepositoryClone> materialization;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_materializations.TryGetValue(key, out materialization!))
            {
                materialization = MaterializeAsync(key, cancellationToken);
                _materializations[key] = materialization;
            }
        }

        return AwaitAndRepairAsync(key, materialization, cancellationToken);
    }

    private async Task<RepositoryClone> AwaitAndRepairAsync(
        RepositoryCloneKey key,
        Task<RepositoryClone> materialization,
        CancellationToken cancellationToken)
    {
        try
        {
            var clone = await materialization.WaitAsync(cancellationToken).ConfigureAwait(false);
            // If a cached entry's directory was deleted out from under us (manual cleanup, disk
            // pressure, transient FS error), evict and reclone rather than handing back a bad path.
            if (!Directory.Exists(clone.RootPath))
            {
                lock (_sync)
                {
                    if (_materializations.TryGetValue(key, out var current) && ReferenceEquals(current, materialization))
                    {
                        _materializations.Remove(key);
                    }
                }
                return await AcquireAsync(key, cancellationToken).ConfigureAwait(false);
            }
            return clone;
        }
        catch
        {
            lock (_sync)
            {
                if (_materializations.TryGetValue(key, out var current) && ReferenceEquals(current, materialization))
                {
                    _materializations.Remove(key);
                }
            }
            throw;
        }
    }

    private async Task<RepositoryClone> MaterializeAsync(RepositoryCloneKey key, CancellationToken cancellationToken)
    {
        var destination = Path.Combine(_rootDirectory, key.DirectorySegment);

        if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
        {
            var existingHead = await _git.RunAsync(
                destination,
                new[] { "rev-parse", "HEAD" },
                cancellationToken).ConfigureAwait(false);
            if (existingHead.Succeeded
                && string.Equals(existingHead.StdOut.Trim(), key.CommitSha, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("Reusing existing clone at {Path} for {Sha}", destination, key.CommitSha);
                return new RepositoryClone(destination, key, key.CommitSha);
            }

            _logger.LogWarning("Replacing invalid cached clone at {Path} for {Sha}", destination, key.CommitSha);
            TryDeleteDirectory(destination);
            if (Directory.Exists(destination))
            {
                throw new InvalidOperationException(
                    $"Invalid cached clone could not be removed at {destination}.");
            }
        }

        // Fresh directory. Delete anything half-materialized from a previous crash so `git clone`
        // doesn't refuse to write into a non-empty target.
        if (Directory.Exists(destination))
        {
            TryDeleteDirectory(destination);
        }
        Directory.CreateDirectory(destination);

        try
        {
            _logger.LogInformation("Cloning {Url} at {Sha} into {Path}", key.RepositoryUrl, key.CommitSha, destination);

            // `--filter=blob:none` sets extensions.partialclone, which F4's RepositoryInspector
            // explicitly rejects. Use a plain shallow clone instead: still one round trip,
            // still one revision, still F4-compatible.
            // `core.autocrlf=false` + `core.eol=lf` keep tracked bytes byte-identical to what git
            // stored in the tree, so F4's raw-blob hash check matches on Windows checkouts.
            var clone = await _git.RunAsync(_rootDirectory, new[]
            {
                "clone",
                "--no-tags",
                "--depth=1",
                "-c", "core.autocrlf=false",
                "-c", "core.eol=lf",
                "-c", "core.longpaths=true",
                key.RepositoryUrl,
                destination,
            }, cancellationToken).ConfigureAwait(false);
            if (!clone.Succeeded)
            {
                throw new InvalidOperationException(
                    $"git clone failed ({clone.ExitCode}) for {Redact(key.RepositoryUrl)}: {Truncate(clone.StdErr)}");
            }

            var fetch = await _git.RunAsync(destination, new[]
            {
                "fetch", "--depth=1", "origin", key.CommitSha,
            }, cancellationToken).ConfigureAwait(false);
            if (!fetch.Succeeded)
            {
                throw new InvalidOperationException(
                    $"git fetch of {key.CommitSha} failed ({fetch.ExitCode}): {Truncate(fetch.StdErr)}");
            }

            var checkout = await _git.RunAsync(destination, new[]
            {
                "checkout", "--detach", key.CommitSha,
            }, cancellationToken).ConfigureAwait(false);
            if (!checkout.Succeeded)
            {
                throw new InvalidOperationException(
                    $"git checkout of {key.CommitSha} failed ({checkout.ExitCode}): {Truncate(checkout.StdErr)}");
            }

            var resolved = await _git.RunAsync(destination, new[] { "rev-parse", "HEAD" }, cancellationToken)
                .ConfigureAwait(false);
            var resolvedSha = resolved.Succeeded ? resolved.StdOut.Trim().ToLowerInvariant() : key.CommitSha;

            return new RepositoryClone(destination, key, resolvedSha);
        }
        catch
        {
            TryDeleteDirectory(destination);
            throw;
        }
    }

    private static void ValidateKey(RepositoryCloneKey key)
    {
        if (!Uri.TryCreate(key.RepositoryUrl, UriKind.Absolute, out var uri) ||
            !uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new ArgumentException("RepositoryUrl must be a credential-free absolute https:// URL.", nameof(key));
        }
        if (string.IsNullOrWhiteSpace(key.CommitSha) || key.CommitSha.Length < 40 ||
            !key.CommitSha.All(IsHex))
        {
            throw new ArgumentException("CommitSha must be a full 40-char hex SHA.", nameof(key));
        }
    }

    private static bool IsHex(char c) =>
        (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

    private static string Redact(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? $"{uri.Scheme}://{uri.Host}{uri.AbsolutePath}"
            : "<redacted>";

    private static string Truncate(string text) =>
        text.Length <= 400 ? text : text[..400] + "…";

    public void Dispose()
    {
        List<Task<RepositoryClone>> outstanding;
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            outstanding = _materializations.Values.ToList();
            _materializations.Clear();
        }

        foreach (var task in outstanding)
        {
            if (task.Status == TaskStatus.RanToCompletion)
            {
                TryDeleteDirectory(task.Result.RootPath);
            }
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (!Directory.Exists(path)) return;

            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(path, recursive: true);
        }
        catch (IOException) { /* best effort */ }
        catch (UnauthorizedAccessException) { /* best effort */ }
    }
}
