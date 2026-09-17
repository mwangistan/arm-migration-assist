using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Validation.Api;

// Wire contract for POST /api/v1/validation/plans/from-git.
// Callers give us a github.com URL + branch (optionally pinned to a sha);
// we clone into an ephemeral scratch, then delegate to the existing
// PrepareAsync + CreatePlanAsync flow with the local path.
public sealed record CreatePlanFromGitRequest(
    [property: JsonPropertyName("migrationPlan")] Validation.BuildValidation.MigrationPlan MigrationPlan,
    [property: JsonPropertyName("source")] GitSource Source,
    [property: JsonPropertyName("includeProposal")] bool IncludeProposal = false);

public sealed record GitSource(
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("branch")] string Branch,
    [property: JsonPropertyName("commitSha")] string? CommitSha = null);

// Minimal safe wrapper for the git subprocess. The clone runs inside the
// container replica's own /tmp (outside /data/validation-api storage root), so
// the existing store guards against evidence-under-storage still hold.
internal static class GitCloner
{
    private const int CloneTimeoutSeconds = 300;

    public sealed record CloneResult(string ScratchRoot, string RepoPath, string EvidencePath, string ResolvedCommitSha);

    public static async Task<CloneResult> CloneAsync(GitSource source, CancellationToken cancellationToken)
    {
        if (!TryValidateSource(source, out var error))
        {
            throw new InvalidDataException(error!);
        }

        var scratch = Path.Combine(Path.GetTempPath(), "validation-git-" + Guid.NewGuid().ToString("N"));
        var repoPath = Path.Combine(scratch, "repo");
        var evidencePath = Path.Combine(scratch, "evidence");
        Directory.CreateDirectory(scratch);
        Directory.CreateDirectory(evidencePath);

        var timeout = TimeSpan.FromSeconds(CloneTimeoutSeconds);

        var cloneArgs = new List<string>
        {
            "clone", "--filter=blob:none", "--no-tags", "--depth=1",
            "--branch", source.Branch, "--single-branch",
            source.Url, "repo"
        };
        var clone = await RunGitAsync(scratch, cloneArgs, timeout, cancellationToken).ConfigureAwait(false);
        if (!clone.Succeeded)
        {
            TryDelete(scratch);
            throw new InvalidDataException($"git clone failed ({clone.ExitCode}): {clone.StdErr.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(source.CommitSha))
        {
            var fetch = await RunGitAsync(repoPath, new[] { "fetch", "--depth=1", "origin", source.CommitSha }, timeout, cancellationToken).ConfigureAwait(false);
            if (!fetch.Succeeded)
            {
                TryDelete(scratch);
                throw new InvalidDataException($"git fetch {source.CommitSha} failed ({fetch.ExitCode}): {fetch.StdErr.Trim()}");
            }
            var checkout = await RunGitAsync(repoPath, new[] { "checkout", "--detach", source.CommitSha }, timeout, cancellationToken).ConfigureAwait(false);
            if (!checkout.Succeeded)
            {
                TryDelete(scratch);
                throw new InvalidDataException($"git checkout {source.CommitSha} failed ({checkout.ExitCode}): {checkout.StdErr.Trim()}");
            }
        }

        var revParse = await RunGitAsync(repoPath, new[] { "rev-parse", "HEAD" }, timeout, cancellationToken).ConfigureAwait(false);
        var resolved = revParse.Succeeded ? revParse.StdOut.Trim() : (source.CommitSha ?? string.Empty);

        return new CloneResult(scratch, repoPath, evidencePath, resolved);
    }

    private static bool TryValidateSource(GitSource source, out string? error)
    {
        error = null;
        if (source is null || string.IsNullOrWhiteSpace(source.Url))
        {
            error = "source.url is required.";
            return false;
        }
        if (!Uri.TryCreate(source.Url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            error = "source.url must be an absolute https:// URL.";
            return false;
        }
        if (!uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
        {
            error = "source.url must be a github.com URL.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(source.Branch) || !IsSafeBranchName(source.Branch))
        {
            error = "source.branch is required and must contain only [A-Za-z0-9_./-] with no leading/trailing '/' or '..'.";
            return false;
        }
        if (!string.IsNullOrWhiteSpace(source.CommitSha))
        {
            if (source.CommitSha.Length < 7 || source.CommitSha.Length > 64 ||
                !source.CommitSha.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
            {
                error = "source.commitSha must be a 7-64 char hex string.";
                return false;
            }
        }
        return true;
    }

    private static bool IsSafeBranchName(string name)
    {
        if (name.StartsWith('/') || name.EndsWith('/')) return false;
        if (name.Contains("..", StringComparison.Ordinal)) return false;
        foreach (var c in name)
        {
            if (!(char.IsLetterOrDigit(c) || c is '-' or '_' or '.' or '/')) return false;
        }
        return true;
    }

    private sealed record ProcessResult(int ExitCode, string StdOut, string StdErr)
    {
        public bool Succeeded => ExitCode == 0;
    }

    private static async Task<ProcessResult> RunGitAsync(
        string workingDirectory, IEnumerable<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in arguments) psi.ArgumentList.Add(a);
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        psi.Environment["GIT_ASKPASS"] = "echo";

        using var process = new Process { StartInfo = psi };
        process.Start();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException($"git {string.Join(' ', arguments)} timed out after {timeout.TotalSeconds:0}s.");
        }

        return new ProcessResult(process.ExitCode,
            await stdoutTask.ConfigureAwait(false),
            await stderrTask.ConfigureAwait(false));
    }

    private static void TryDelete(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { }
    }
}
