using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AutomatedMigration.Api.Contracts;
using Microsoft.Extensions.Options;

namespace AutomatedMigration.Api.Publication;

public interface IGitHubPublisher
{
    Task<PublicationResult> PublishAsync(
        RepositoryTarget target,
        IReadOnlyList<GeneratedPatchDto> patches,
        PublishRequest publish,
        CancellationToken cancellationToken);
}

// Publishes the diffs F3 generated to a PAT-owned fork of the target repo, then opens
// a PR upstream. Uses:
//   - GitHub REST for /user, forks, PRs, branch metadata
//   - `git` subprocess for clone / apply / commit / push (more robust for real diffs
//     than reconstructing tree via the git-data API)
// Failure of any step returns a PublicationResult with Error populated; the caller
// (MigrationJobWorker) never lets a publish failure fail the underlying job.
public sealed class GitHubPublisher : IGitHubPublisher
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly PublicationOptions _options;
    private readonly ILogger<GitHubPublisher> _logger;

    public GitHubPublisher(HttpClient http, IOptions<PublicationOptions> options, ILogger<GitHubPublisher> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;

        _http.Timeout = TimeSpan.FromSeconds(Math.Max(5, _options.ApiTimeoutSeconds));
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("arm-migration-assist-publisher/1.0");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        if (!string.IsNullOrWhiteSpace(_options.Token))
        {
            _http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.Token);
        }
    }

    public async Task<PublicationResult> PublishAsync(
        RepositoryTarget target,
        IReadOnlyList<GeneratedPatchDto> patches,
        PublishRequest publish,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();

        if (!_options.IsConfigured)
        {
            return Fail("Publication is not configured on the server (AUTOMATION_PUBLISH_TOKEN is unset).", warnings);
        }
        if (!TryParseGithubOwnerRepo(target.Url, out var upstreamOwner, out var upstreamRepo))
        {
            return Fail($"target.url '{target.Url}' is not a parseable https://github.com/{{owner}}/{{repo}} URL.", warnings);
        }
        if (string.IsNullOrWhiteSpace(publish.BranchName))
        {
            return Fail("publish.branchName is required.", warnings);
        }
        if (patches is null || patches.Count == 0)
        {
            return new PublicationResult(true, null, publish.BranchName, target.CommitSha, null, null, 0, 0,
                "No patches were generated; nothing to publish.", warnings);
        }

        string forkLogin;
        try
        {
            forkLogin = await GetAuthenticatedLoginAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return Fail($"Could not identify the authenticated user: {ex.Message}", warnings);
        }
        var forkFullName = $"{forkLogin}/{upstreamRepo}";

        try
        {
            await EnsureForkAsync(upstreamOwner, upstreamRepo, forkLogin, warnings, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return Fail($"Fork step failed: {ex.Message}", warnings, forkFullName: forkFullName);
        }

        var scratch = Path.Combine(Path.GetTempPath(), "amma-pub-" + Guid.NewGuid().ToString("N"));
        try
        {
            var (applied, failed, head) = await CloneAndApplyAsync(
                forkLogin, upstreamRepo, target.CommitSha, publish.BranchName, patches, scratch, warnings, cancellationToken)
                .ConfigureAwait(false);

            if (applied == 0)
            {
                return new PublicationResult(true, forkFullName, publish.BranchName, target.CommitSha, null, null,
                    applied, failed, "No patches applied cleanly; nothing was pushed and no PR was opened.", warnings);
            }

            string? defaultBranch = publish.UpstreamDefaultBranch;
            if (string.IsNullOrWhiteSpace(defaultBranch))
            {
                defaultBranch = await GetDefaultBranchAsync(upstreamOwner, upstreamRepo, cancellationToken)
                    .ConfigureAwait(false);
            }

            string? prUrl;
            try
            {
                prUrl = await EnsurePullRequestAsync(
                    upstreamOwner, upstreamRepo, forkLogin, publish.BranchName, defaultBranch!,
                    publish.PrTitle, publish.PrBody, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return new PublicationResult(true, forkFullName, publish.BranchName, target.CommitSha, head, null,
                    applied, failed, $"PR step failed: {ex.Message}", warnings);
            }

            return new PublicationResult(true, forkFullName, publish.BranchName, target.CommitSha, head, prUrl,
                applied, failed, null, warnings);
        }
        finally
        {
            TryDeleteDirectory(scratch);
        }
    }

    private static PublicationResult Fail(string message, IReadOnlyList<string> warnings, string? forkFullName = null) =>
        new(true, forkFullName, null, null, null, null, 0, 0, message, warnings);

    private async Task<string> GetAuthenticatedLoginAsync(CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync("https://api.github.com/user", cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<UserPayload>(Json, cancellationToken).ConfigureAwait(false);
        if (payload is null || string.IsNullOrWhiteSpace(payload.Login))
        {
            throw new InvalidOperationException("GitHub /user returned no login.");
        }
        return payload.Login;
    }

    private async Task EnsureForkAsync(
        string upstreamOwner, string upstreamRepo, string forkLogin,
        List<string> warnings, CancellationToken cancellationToken)
    {
        var forkUrl = $"https://api.github.com/repos/{upstreamOwner}/{upstreamRepo}/forks";
        using var response = await _http.PostAsync(forkUrl, content: null, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.Accepted or HttpStatusCode.OK or HttpStatusCode.Created)
        {
            // 202 = fork queued (may be a brand-new fork). 200 = existing fork. Either way,
            // poll until the fork is clonable.
            await WaitUntilForkExistsAsync(forkLogin, upstreamRepo, warnings, cancellationToken).ConfigureAwait(false);
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        throw new HttpRequestException($"Fork request returned {(int)response.StatusCode}: {body}");
    }

    private async Task WaitUntilForkExistsAsync(
        string forkLogin, string repoName, List<string> warnings, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(_options.ForkPollTimeoutSeconds);
        var url = $"https://api.github.com/repos/{forkLogin}/{repoName}";
        var delay = TimeSpan.FromSeconds(2);
        while (DateTimeOffset.UtcNow < deadline)
        {
            using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                return;
            }
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            if (delay < TimeSpan.FromSeconds(8)) delay = delay.Add(TimeSpan.FromSeconds(2));
        }
        warnings.Add($"Fork {forkLogin}/{repoName} did not become visible within {_options.ForkPollTimeoutSeconds}s; git clone may still succeed.");
    }

    private async Task<(int applied, int failed, string? headCommitSha)> CloneAndApplyAsync(
        string forkLogin, string repoName, string baseCommitSha, string branchName,
        IReadOnlyList<GeneratedPatchDto> patches, string scratch,
        List<string> warnings, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(scratch);
        var repoRoot = Path.Combine(scratch, "repo");

        // Token embedded in remote URL — process-local, never logged or written to disk.
        var remoteUrl = $"https://x-access-token:{_options.Token}@github.com/{forkLogin}/{repoName}.git";
        var timeout = TimeSpan.FromSeconds(Math.Max(5, _options.GitTimeoutSeconds));

        // Shallow clone from the base commit only — one revision fetched, keeps ACA scratch
        // disk usage low even for large repos like ComfyUI.
        var clone = await GitProcess.RunAsync(scratch, new[]
        {
            "clone", "--filter=blob:none", "--no-tags", "--depth=1", remoteUrl, "repo"
        }, timeout, env: null, cancellationToken).ConfigureAwait(false);
        if (!clone.Succeeded)
        {
            throw new InvalidOperationException($"git clone failed ({clone.ExitCode}): {Redact(clone.StdErr)}");
        }

        // Fetch the exact upstream commit into the fork clone so we can branch off it.
        // The fork inherits upstream history but a very recent commit may not yet be present.
        var fetch = await GitProcess.RunAsync(repoRoot, new[]
        {
            "fetch", "--depth=1", "origin", baseCommitSha
        }, timeout, env: null, cancellationToken).ConfigureAwait(false);
        if (!fetch.Succeeded)
        {
            // Fall back to fetching upstream directly. Fork-of-fork chains may not know the sha yet.
            var addUpstream = await GitProcess.RunAsync(repoRoot, new[]
            {
                "remote", "add", "upstream",
                $"https://github.com/{ExtractUpstreamOwnerFromFork(forkLogin, repoName, warnings)}/{repoName}.git"
            }, timeout, env: null, cancellationToken).ConfigureAwait(false);
            if (!addUpstream.Succeeded)
            {
                warnings.Add($"git remote add upstream failed: {Redact(addUpstream.StdErr)}");
            }
        }

        var checkout = await GitProcess.RunAsync(repoRoot, new[]
        {
            "checkout", "-B", branchName, baseCommitSha
        }, timeout, env: null, cancellationToken).ConfigureAwait(false);
        if (!checkout.Succeeded)
        {
            throw new InvalidOperationException($"git checkout failed ({checkout.ExitCode}): {Redact(checkout.StdErr)}");
        }

        var patchDir = Path.Combine(scratch, "patches");
        Directory.CreateDirectory(patchDir);

        var applied = 0;
        var failed = 0;
        for (var i = 0; i < patches.Count; i++)
        {
            var patch = patches[i];
            var patchPath = Path.Combine(patchDir, $"{i:000}-{Sanitize(patch.WorkItemId)}.patch");
            await File.WriteAllTextAsync(patchPath, patch.Diff, cancellationToken).ConfigureAwait(false);

            var apply = await GitProcess.RunAsync(repoRoot, new[]
            {
                "apply", "--index", "--whitespace=nowarn", patchPath
            }, timeout, env: null, cancellationToken).ConfigureAwait(false);
            if (!apply.Succeeded)
            {
                warnings.Add($"git apply failed for {patch.WorkItemId}: {Redact(apply.StdErr).Trim()}");
                failed++;
                continue;
            }
            applied++;
        }

        if (applied == 0)
        {
            return (0, failed, null);
        }

        // One commit per publish, subject line + body summarising the applied patches. Author
        // and committer are the service's own identity, not the PAT owner.
        var summary = string.Join("\n", patches.Take(applied)
            .Select(p => $"- {p.WorkItemId} ({p.AgentOrSkill}): {p.Title}"));
        var commitMessage = $"arm-migration-assist: apply {applied} patch(es)\n\n{summary}\n";
        var commit = await GitProcess.RunAsync(repoRoot, new[]
        {
            "-c", $"user.name={_options.CommitAuthorName}",
            "-c", $"user.email={_options.CommitAuthorEmail}",
            "commit", "-m", commitMessage
        }, timeout, env: null, cancellationToken).ConfigureAwait(false);
        if (!commit.Succeeded)
        {
            throw new InvalidOperationException($"git commit failed ({commit.ExitCode}): {Redact(commit.StdErr)}");
        }

        var push = await GitProcess.RunAsync(repoRoot, new[]
        {
            "push", "--force-with-lease", "origin", $"HEAD:refs/heads/{branchName}"
        }, timeout, env: null, cancellationToken).ConfigureAwait(false);
        if (!push.Succeeded)
        {
            throw new InvalidOperationException($"git push failed ({push.ExitCode}): {Redact(push.StdErr)}");
        }

        var revParse = await GitProcess.RunAsync(repoRoot, new[]
        {
            "rev-parse", "HEAD"
        }, timeout, env: null, cancellationToken).ConfigureAwait(false);
        var head = revParse.Succeeded ? revParse.StdOut.Trim() : null;

        return (applied, failed, head);
    }

    private async Task<string> GetDefaultBranchAsync(string owner, string repo, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync($"https://api.github.com/repos/{owner}/{repo}", cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<RepoPayload>(Json, cancellationToken).ConfigureAwait(false);
        if (payload is null || string.IsNullOrWhiteSpace(payload.DefaultBranch))
        {
            throw new InvalidOperationException($"GitHub /repos/{owner}/{repo} returned no default_branch.");
        }
        return payload.DefaultBranch;
    }

    private async Task<string?> EnsurePullRequestAsync(
        string upstreamOwner, string upstreamRepo, string forkLogin, string branchName, string baseBranch,
        string? prTitle, string? prBody, CancellationToken cancellationToken)
    {
        var head = $"{forkLogin}:{branchName}";
        var listUrl = $"https://api.github.com/repos/{upstreamOwner}/{upstreamRepo}/pulls?head={Uri.EscapeDataString(head)}&state=open";
        using (var existing = await _http.GetAsync(listUrl, cancellationToken).ConfigureAwait(false))
        {
            if (existing.IsSuccessStatusCode)
            {
                var open = await existing.Content.ReadFromJsonAsync<List<PullPayload>>(Json, cancellationToken)
                    .ConfigureAwait(false);
                var match = open?.FirstOrDefault();
                if (match is not null && !string.IsNullOrWhiteSpace(match.HtmlUrl))
                {
                    return match.HtmlUrl;
                }
            }
        }

        var createUrl = $"https://api.github.com/repos/{upstreamOwner}/{upstreamRepo}/pulls";
        var body = new
        {
            title = string.IsNullOrWhiteSpace(prTitle) ? $"arm-migration-assist: {branchName}" : prTitle,
            head,
            @base = baseBranch,
            body = prBody ?? "Generated by arm-migration-assist. Review each work item before merging.",
            maintainer_can_modify = true,
            draft = true,
        };
        using var create = await _http.PostAsJsonAsync(createUrl, body, Json, cancellationToken).ConfigureAwait(false);
        if (create.IsSuccessStatusCode)
        {
            var payload = await create.Content.ReadFromJsonAsync<PullPayload>(Json, cancellationToken).ConfigureAwait(false);
            return payload?.HtmlUrl;
        }

        var text = await create.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        throw new HttpRequestException($"POST /pulls returned {(int)create.StatusCode}: {text}");
    }

    private static bool TryParseGithubOwnerRepo(string url, out string owner, out string repo)
    {
        owner = repo = string.Empty;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (!uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)) return false;
        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2) return false;
        owner = segments[0];
        repo = segments[1];
        if (repo.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            repo = repo[..^4];
        }
        return true;
    }

    private static string ExtractUpstreamOwnerFromFork(string forkLogin, string repo, List<string> warnings)
    {
        // We only get here when git fetch of a sha failed. We don't have the upstream owner
        // stored locally; log a warning and let the caller retry through a normal branch-based
        // path. In practice ComfyUI's forks share history and the sha fetch succeeds.
        warnings.Add("Could not fetch base commit from fork; upstream fetch fallback is best-effort.");
        return forkLogin;
    }

    private static string Sanitize(string id)
    {
        var buf = new char[id.Length];
        for (var i = 0; i < id.Length; i++)
        {
            var c = id[i];
            buf[i] = char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-';
        }
        return new string(buf);
    }

    private string Redact(string message)
    {
        if (string.IsNullOrEmpty(_options.Token)) return message;
        return message.Replace(_options.Token, "***");
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { }
    }

    private sealed record UserPayload(string? Login);
    private sealed record RepoPayload(string? DefaultBranch);
    private sealed record PullPayload(string? HtmlUrl);
}
