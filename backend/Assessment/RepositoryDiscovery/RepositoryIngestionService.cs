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
    private readonly TimeSpan _cacheTtl;
    private readonly ILogger<RepositoryIngestionService> _logger;

    public RepositoryIngestionService(IWebHostEnvironment env, ILogger<RepositoryIngestionService> logger)
    {
        _workspaceRoot = Environment.GetEnvironmentVariable("ARM_MIGRATION_WORKSPACE_ROOT")
            ?? Path.Combine(env.ContentRootPath, ".arm-ma");
        Directory.CreateDirectory(_workspaceRoot);

        _cacheTtl = double.TryParse(
                Environment.GetEnvironmentVariable("ARM_MIGRATION_CACHE_TTL_HOURS"),
                out var hours) && hours > 0
            ? TimeSpan.FromHours(hours)
            : TimeSpan.FromDays(7);

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

        TryEvictStaleWorkspaces(cacheKey);

        bool fromCache = Directory.Exists(Path.Combine(localRepoPath, ".git"));
        if (fromCache)
        {
            _logger.LogInformation("Reusing cached clone for {RepoUrl}", repoUrl);
            var (fetchCode, _, fetchErr) = await RunGitAsync(localRepoPath, "fetch --depth 1 origin", ct);
            if (fetchCode != 0)
            {
                // Gap 4: a failed refresh must not silently serve a stale/broken cache - fall back to a fresh clone.
                _logger.LogWarning("Cache refresh failed for {RepoUrl}; re-cloning. {Error}",
                    repoUrl, fetchErr.Trim());
                fromCache = false;
            }
            else
            {
                // Gap 3: move the working tree to the freshly fetched tip so CommitSha and files reflect upstream.
                await RunGitAsync(localRepoPath, "reset --hard FETCH_HEAD", ct);
            }
        }

        if (!fromCache)
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
        var commitDateRaw = (await RunGitAsync(localRepoPath, "log -1 --format=%cI", ct)).StdOut.Trim();
        var lastCommitDate = DateTimeOffset.TryParse(commitDateRaw, out var parsed) ? parsed : (DateTimeOffset?)null;

        var files = RepoFiles.Enumerate(localRepoPath).ToList();
        long sizeBytes = 0;
        foreach (var f in files)
        {
            try { sizeBytes += new FileInfo(f).Length; }
            catch (IOException) { /* file vanished between enumeration and stat; ignore */ }
        }

        return new RepositorySnapshot
        {
            RunId = runId,
            RepoUrl = repoUrl,
            WorkspacePath = workspacePath,
            LocalRepoPath = localRepoPath,
            CommitSha = string.IsNullOrEmpty(commitSha) ? null : commitSha,
            DefaultBranch = string.IsNullOrEmpty(branch) ? null : branch,
            FileCount = files.Count,
            ClonedAt = DateTimeOffset.UtcNow,
            FromCache = fromCache,
            License = DetectLicense(localRepoPath),
            LastCommitDate = lastCommitDate,
            SizeBytes = sizeBytes
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

    /// <summary>
    /// Gap 3: best-effort eviction so the workspace cache does not grow without bound.
    /// Deletes cached clones older than the configured TTL, skipping the one about to be used.
    /// Failures (e.g. a folder locked by a concurrent run) are ignored.
    /// </summary>
    private void TryEvictStaleWorkspaces(string currentCacheKey)
    {
        try
        {
            var cutoff = DateTime.UtcNow - _cacheTtl;
            foreach (var dir in Directory.EnumerateDirectories(_workspaceRoot))
            {
                if (string.Equals(Path.GetFileName(dir), currentCacheKey, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (Directory.GetLastWriteTimeUtc(dir) >= cutoff)
                    continue;

                try
                {
                    Directory.Delete(dir, recursive: true);
                    _logger.LogInformation("Evicted stale workspace {Workspace}", Path.GetFileName(dir));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _logger.LogDebug(ex, "Could not evict workspace {Workspace}", Path.GetFileName(dir));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Workspace eviction sweep failed");
        }
    }

    private static readonly string[] LicenseFileNames =
    [
        "LICENSE", "LICENSE.md", "LICENSE.txt", "LICENSE-MIT", "LICENCE",
        "LICENCE.md", "LICENCE.txt", "COPYING", "COPYING.md", "UNLICENSE"
    ];

    /// <summary>
    /// Gap 2: capture license metadata deterministically and offline by inspecting the checked-out
    /// license file. Recognizes common licenses and maps them to an SPDX identifier; falls back to
    /// the file name when the text is unrecognized, or null when no license file exists.
    /// </summary>
    internal static string? DetectLicense(string repoPath)
    {
        string? licenseFile = null;
        foreach (var name in LicenseFileNames)
        {
            var candidate = Path.Combine(repoPath, name);
            if (File.Exists(candidate)) { licenseFile = candidate; break; }
        }

        if (licenseFile is null)
            return null;

        string text;
        try { text = File.ReadAllText(licenseFile); }
        catch (IOException) { return Path.GetFileName(licenseFile); }

        return IdentifyLicense(text) ?? Path.GetFileName(licenseFile);
    }

    internal static string? IdentifyLicense(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var t = text.ToLowerInvariant();

        if (t.Contains("apache license") && t.Contains("version 2.0")) return "Apache-2.0";
        if (t.Contains("gnu affero general public license") && t.Contains("version 3")) return "AGPL-3.0";
        if (t.Contains("gnu lesser general public license") && t.Contains("version 3")) return "LGPL-3.0";
        if (t.Contains("gnu general public license") && t.Contains("version 3")) return "GPL-3.0";
        if (t.Contains("gnu general public license") && t.Contains("version 2")) return "GPL-2.0";
        if (t.Contains("mozilla public license") && t.Contains("version 2.0")) return "MPL-2.0";
        if (t.Contains("this is free and unencumbered software released into the public domain")) return "Unlicense";
        if (t.Contains("permission is hereby granted, free of charge") && t.Contains("mit")) return "MIT";
        if (t.Contains("permission is hereby granted, free of charge")) return "MIT";
        if (t.Contains("redistribution and use") && t.Contains("neither the name")) return "BSD-3-Clause";
        if (t.Contains("redistribution and use")) return "BSD-2-Clause";
        if (t.Contains("boost software license")) return "BSL-1.0";
        if (t.Contains("isc license") || (t.Contains("permission to use, copy, modify") && t.Contains("isc"))) return "ISC";

        return null;
    }

    internal static string FormatCloneError(string error)
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
