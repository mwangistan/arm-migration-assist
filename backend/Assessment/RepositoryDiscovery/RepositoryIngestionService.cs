using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace ArmMigrationAssist.Api.Assessment.RepositoryDiscovery;

/// <summary>
/// Story 1.1 - Repository Ingestion.
/// Clones a public GitHub repository (shallow) into an isolated, per-repo workspace,
/// captures metadata, and caches by repository URL so re-runs are fast.
/// Ingestion is a prerequisite for the skills, so it is invoked directly by the orchestrator
/// (it produces the <see cref="RepositorySnapshot"/> that every skill consumes).
/// </summary>
public sealed class RepositoryIngestionService
{
    private readonly string _workspaceRoot;
    private readonly ILogger<RepositoryIngestionService> _logger;

    public RepositoryIngestionService(IWebHostEnvironment env, ILogger<RepositoryIngestionService> logger)
    {
        _workspaceRoot = Environment.GetEnvironmentVariable("ARM_MIGRATION_WORKSPACE_ROOT")
            ?? Path.Combine(env.ContentRootPath, ".arm-ma");
        Directory.CreateDirectory(_workspaceRoot);
        _logger = logger;
    }

    public async Task<RepositorySnapshot> IngestAsync(string repoUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(repoUrl))
            throw new ArgumentException("Repository URL is required.", nameof(repoUrl));
        if (!IsPublicGitHubRepository(repoUrl))
            throw new ArgumentException(
                "Repository URL must be a public HTTPS GitHub URL in the form https://github.com/owner/repository.",
                nameof(repoUrl));

        var runId = Guid.NewGuid().ToString("N");
        var cacheKey = Hash(repoUrl.Trim().TrimEnd('/'));
        var workspacePath = Path.Combine(_workspaceRoot, cacheKey);
        var localRepoPath = Path.Combine(workspacePath, "r");
        Directory.CreateDirectory(workspacePath);

        bool fromCache = Directory.Exists(Path.Combine(localRepoPath, ".git"));
        if (fromCache)
        {
            _logger.LogInformation("Reusing cached clone for {RepoUrl}", repoUrl);
            await RunGitAsync(localRepoPath, "fetch --depth 1 origin", ct);
        }
        else
        {
            if (Directory.Exists(localRepoPath))
                Directory.Delete(localRepoPath, recursive: true);

            var (code, _, err) = await RunGitAsync(workspacePath,
                $"clone --depth 1 {Quote(repoUrl)} r", ct);
            if (code != 0)
                throw new InvalidOperationException(FormatCloneError(err));
        }

        var commitSha = (await RunGitAsync(localRepoPath, "rev-parse HEAD", ct)).StdOut.Trim();
        var branch = (await RunGitAsync(localRepoPath, "rev-parse --abbrev-ref HEAD", ct)).StdOut.Trim();
        var fileCount = RepoFiles.Enumerate(localRepoPath).Count();

        return new RepositorySnapshot
        {
            RunId = runId,
            RepoUrl = repoUrl,
            WorkspacePath = workspacePath,
            LocalRepoPath = localRepoPath,
            CommitSha = string.IsNullOrEmpty(commitSha) ? null : commitSha,
            DefaultBranch = string.IsNullOrEmpty(branch) ? null : branch,
            FileCount = fileCount,
            ClonedAt = DateTimeOffset.UtcNow,
            FromCache = fromCache
        };
    }

    private static string Quote(string s) => $"\"{s}\"";

    internal static bool IsPublicGitHubRepository(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !uri.IsDefaultPort ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
            return false;

        var parts = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 && parts.All(p => p is not "." and not "..");
    }

    private static string Hash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }

    private static string FormatCloneError(string error)
    {
        if (error.Contains("Filename too long", StringComparison.OrdinalIgnoreCase))
            return "git clone failed because Windows rejected long repository paths. " +
                   "The assessor enabled Git long-path support, but this machine may also require " +
                   "the Windows LongPathsEnabled policy.";

        var lines = error.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        var useful = lines.Where(line =>
                line.Contains("error:", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("fatal:", StringComparison.OrdinalIgnoreCase))
            .TakeLast(5);
        var detail = string.Join(" ", useful);
        return string.IsNullOrWhiteSpace(detail) ? "git clone failed." : $"git clone failed: {detail}";
    }

    private static async Task<(int Code, string StdOut, string StdErr)> RunGitAsync(
        string workingDir, string arguments, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("git", $"-c core.longpaths=true {arguments}")
        {
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };
        process.Start();
        var stdOutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stdErrTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        return (process.ExitCode, await stdOutTask, await stdErrTask);
    }
}
