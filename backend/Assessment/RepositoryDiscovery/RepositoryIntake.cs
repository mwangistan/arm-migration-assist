using System.Text.RegularExpressions;
using ArmMigrationAssist.RepositoryDiscovery.GitHub;
using ArmMigrationAssist.RepositoryWorkspace;

namespace ArmMigrationAssist.RepositoryDiscovery;

internal sealed partial class RepositoryIntake(
    GitClient git,
    IGitHubRepositorySource gitHubRepositorySource,
    bool useStoredGitHubCredentials,
    IRepositoryClonePool? clonePool = null,
    IGitHubMetadataResolver? metadataResolver = null)
{
    public async Task<RepositoryWorkspace> OpenAsync(
        string source,
        CancellationToken cancellationToken)
    {
        var trimmedSource = source.Trim();
        if (LooksLikeUrl(trimmedSource))
        {
            var repositoryUrl = NormalizeGitHubUrl(trimmedSource);

            // Anonymous URL intake with an available shared clone pool: resolve metadata,
            // then reuse the process-lifetime clone. This is what lets a subsequent F3 job
            // for the same commit skip the fetch entirely. Authenticated (loopback) intake
            // stays on the archive path so the token-carrying flow is untouched.
            if (clonePool is not null && metadataResolver is not null && !useStoredGitHubCredentials)
            {
                var metadata = await metadataResolver.ResolveMetadataAsync(
                    repositoryUrl, useStoredCredentials: false, cancellationToken);
                var clone = await clonePool.AcquireAsync(
                    new RepositoryCloneKey(repositoryUrl, metadata.CommitSha, Anonymous: true),
                    cancellationToken);
                return new RepositoryWorkspace(
                    clone.RootPath,
                    repositoryUrl,
                    metadata.Name,
                    clone.ResolvedCommitSha,
                    metadata.DefaultBranch,
                    TemporaryRoot: null,
                    KnownRelativePaths: null,
                    KnownTotalFiles: null,
                    KnownSkippedFiles: 0);
            }

            var temporaryRoot = Path.Combine(
                Path.GetTempPath(),
                "arm-migration-assist",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporaryRoot);

            try
            {
                var snapshot = await gitHubRepositorySource.DownloadAsync(
                    repositoryUrl,
                    temporaryRoot,
                    useStoredGitHubCredentials,
                    cancellationToken);
                return new RepositoryWorkspace(
                    snapshot.Archive.RootPath,
                    repositoryUrl,
                    snapshot.Name,
                    snapshot.CommitSha,
                    snapshot.DefaultBranch,
                    temporaryRoot,
                    snapshot.Archive.RelativePaths,
                    snapshot.Archive.TotalFiles,
                    snapshot.Archive.SkippedFiles);
            }
            catch
            {
                DeleteDirectory(temporaryRoot);
                throw;
            }
        }

        if (!Directory.Exists(trimmedSource))
        {
            throw new RepositoryDiscoveryException("The local repository directory does not exist.");
        }

        var requestedPath = Path.GetFullPath(trimmedSource);
        var rootPath = (await git.RunAsync(
            requestedPath,
            ["rev-parse", "--show-toplevel"],
            cancellationToken)).Trim();

        var dirtyState = await git.RunAsync(
            rootPath,
            ["status", "--porcelain=v1", "--untracked-files=no"],
            cancellationToken);
        if (!string.IsNullOrWhiteSpace(dirtyState))
        {
            throw new RepositoryDiscoveryException(
                "The repository has tracked changes. Commit or stash them so the assessment matches a commit.");
        }

        var commitSha = (await git.RunAsync(
            rootPath,
            ["rev-parse", "HEAD"],
            cancellationToken)).Trim().ToLowerInvariant();
        if (!CommitShaRegex().IsMatch(commitSha))
        {
            throw new RepositoryDiscoveryException("Git returned an invalid commit identifier.");
        }

        var localRepositoryUrl = await ReadLocalRepositoryUrlAsync(rootPath, cancellationToken);
        var defaultBranch = await ReadDefaultBranchAsync(rootPath, cancellationToken);
        if (defaultBranch.Length > 200)
        {
            throw new RepositoryDiscoveryException("The repository default branch name exceeds the assessment contract limit.");
        }

        var name = new Uri(localRepositoryUrl).Segments[^1].Trim('/');
        if (name.Length > 200)
        {
            throw new RepositoryDiscoveryException("The repository name exceeds the assessment contract limit.");
        }

        return new RepositoryWorkspace(
            rootPath,
            localRepositoryUrl,
            name,
            commitSha,
            defaultBranch,
            null,
            null,
            null,
            0);
    }

    internal static string NormalizeGitHubUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
            || !uri.IsDefaultPort
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new RepositoryDiscoveryException(
                "Repository URLs must be anonymous HTTPS GitHub URLs in the form https://github.com/owner/repository.");
        }

        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 2)
        {
            throw new RepositoryDiscoveryException(
                "Repository URLs must identify one GitHub owner and repository.");
        }

        var owner = segments[0];
        var repository = segments[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase)
            ? segments[1][..^4]
            : segments[1];
        if (!GitHubNameRegex().IsMatch(owner) || !GitHubNameRegex().IsMatch(repository))
        {
            throw new RepositoryDiscoveryException("The GitHub owner or repository name is invalid.");
        }

        var normalizedUrl = $"https://github.com/{owner}/{repository}";
        if (repository.Length > 200 || normalizedUrl.Length > 2048)
        {
            throw new RepositoryDiscoveryException("The GitHub repository identity exceeds the assessment contract limit.");
        }

        return normalizedUrl;
    }

    private async Task<string> ReadLocalRepositoryUrlAsync(
        string rootPath,
        CancellationToken cancellationToken)
    {
        var remote = (await git.RunAsync(
            rootPath,
            ["remote", "get-url", "origin"],
            cancellationToken)).Trim();

        var sshMatch = GitHubSshRegex().Match(remote);
        if (sshMatch.Success)
        {
            remote = $"https://github.com/{sshMatch.Groups["owner"].Value}/{sshMatch.Groups["repository"].Value}";
        }

        return NormalizeGitHubUrl(remote);
    }

    private async Task<string> ReadDefaultBranchAsync(
        string rootPath,
        CancellationToken cancellationToken)
    {
        var symbolicRemote = await git.TryRunAsync(
            rootPath,
            ["symbolic-ref", "--quiet", "--short", "refs/remotes/origin/HEAD"],
            cancellationToken);
        if (symbolicRemote.ExitCode == 0)
        {
            var remoteBranch = symbolicRemote.StandardOutput.Trim();
            if (remoteBranch.StartsWith("origin/", StringComparison.Ordinal))
            {
                return remoteBranch["origin/".Length..];
            }
        }

        var currentBranch = (await git.RunAsync(
            rootPath,
            ["branch", "--show-current"],
            cancellationToken)).Trim();
        return string.IsNullOrWhiteSpace(currentBranch) ? "HEAD" : currentBranch;
    }

    private static bool LooksLikeUrl(string source) =>
        source.Contains("://", StringComparison.Ordinal)
        || source.StartsWith("git@", StringComparison.OrdinalIgnoreCase);

    internal static void DeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(path, recursive: true);
    }

    [GeneratedRegex("^([0-9a-f]{40}|[0-9a-f]{64})$", RegexOptions.CultureInvariant)]
    private static partial Regex CommitShaRegex();

    [GeneratedRegex("^[A-Za-z0-9_.-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex GitHubNameRegex();

    [GeneratedRegex("^(?:git@github\\.com:|ssh://git@github\\.com/)(?<owner>[A-Za-z0-9_.-]+)/(?<repository>[A-Za-z0-9_.-]+?)(?:\\.git)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GitHubSshRegex();
}

internal sealed record RepositoryWorkspace(
    string RootPath,
    string RepositoryUrl,
    string Name,
    string CommitSha,
    string DefaultBranch,
    string? TemporaryRoot,
    IReadOnlyList<string>? KnownRelativePaths,
    int? KnownTotalFiles,
    int KnownSkippedFiles) : IDisposable
{
    public void Dispose()
    {
        if (TemporaryRoot is not null)
        {
            RepositoryIntake.DeleteDirectory(TemporaryRoot);
        }
    }
}