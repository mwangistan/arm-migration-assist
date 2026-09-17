using System.Collections.Concurrent;
using System.Diagnostics;

namespace ArmMigrationAssist.RepositoryDiscovery.Authentication;

public interface IGitHubAuthenticationBroker
{
    GitHubAuthenticationSession Start();

    GitHubAuthenticationSession? Get(string sessionId);

    bool IsAuthorized(string sessionId);

    void Invalidate(string sessionId);

    bool Cancel(string sessionId);
}

public sealed record GitHubAuthenticationSession(
    string SessionId,
    string Status,
    DateTimeOffset ExpiresAt,
    string? Message);

internal sealed class GitHubAuthenticationBroker : IGitHubAuthenticationBroker
{
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromMinutes(10);
    private readonly ConcurrentDictionary<string, GitHubAuthenticationSession> sessions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> sessionCancellations = new(StringComparer.Ordinal);
    private readonly object sessionLock = new();
    private string? activeSessionId;

    public GitHubAuthenticationSession Start()
    {
        lock (sessionLock)
        {
            RemoveExpiredSessions();
            if (activeSessionId is not null
                && sessions.TryGetValue(activeSessionId, out var active)
                && active.Status is "pending" or "succeeded")
            {
                return active;
            }

            var session = new GitHubAuthenticationSession(
                Guid.NewGuid().ToString("N"),
                "pending",
                DateTimeOffset.UtcNow.Add(SessionLifetime),
                "Complete GitHub sign-in in the browser window.");
            sessions[session.SessionId] = session;
            var cancellation = new CancellationTokenSource();
            sessionCancellations[session.SessionId] = cancellation;
            activeSessionId = session.SessionId;
            _ = AuthenticateSafelyAsync(session, cancellation.Token);
            return session;
        }
    }

    public GitHubAuthenticationSession? Get(string sessionId)
    {
        if (!IsValidSessionId(sessionId)
            || !sessions.TryGetValue(sessionId, out var session))
        {
            return null;
        }

        if (session.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            Cancel(sessionId);
            sessions.TryRemove(sessionId, out _);
            return null;
        }

        return session;
    }

    public bool IsAuthorized(string sessionId) => Get(sessionId)?.Status == "succeeded";

    public void Invalidate(string sessionId)
    {
        if (!IsValidSessionId(sessionId))
        {
            return;
        }

        if (sessions.TryGetValue(sessionId, out var session))
        {
            sessions[sessionId] = session with
            {
                Status = "failed",
                ExpiresAt = DateTimeOffset.UtcNow,
                Message = "The GitHub account could not access the repository.",
            };
        }
    }

    public bool Cancel(string sessionId)
    {
        if (!IsValidSessionId(sessionId)
            || !sessions.TryGetValue(sessionId, out var session)
            || session.Status != "pending")
        {
            return false;
        }

        sessions[sessionId] = session with
        {
            Status = "failed",
            ExpiresAt = DateTimeOffset.UtcNow,
            Message = "GitHub sign-in was canceled.",
        };
        if (sessionCancellations.TryGetValue(sessionId, out var cancellation))
        {
            try
            {
                cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Authentication completed while cancellation was being requested.
            }
        }

        return true;
    }

    private async Task AuthenticateSafelyAsync(
        GitHubAuthenticationSession session,
        CancellationToken cancellationToken)
    {
        try
        {
            await AuthenticateAsync(session, cancellationToken);
        }
        catch (Exception)
        {
            SetFailed(session, "GitHub sign-in failed unexpectedly.");
        }
        finally
        {
            if (sessionCancellations.TryRemove(session.SessionId, out var cancellation))
            {
                cancellation.Dispose();
            }
        }
    }

    private async Task AuthenticateAsync(
        GitHubAuthenticationSession session,
        CancellationToken cancellationToken)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    RedirectStandardError = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };
            process.StartInfo.ArgumentList.Add("credential-manager");
            process.StartInfo.ArgumentList.Add("github");
            process.StartInfo.ArgumentList.Add("login");
            process.StartInfo.ArgumentList.Add("--url");
            process.StartInfo.ArgumentList.Add("https://github.com");
            process.StartInfo.ArgumentList.Add("--browser");
            process.StartInfo.ArgumentList.Add("--force");

            if (!process.Start())
            {
                SetFailed(session, "Git Credential Manager could not be started.");
                return;
            }

            process.StandardInput.Close();
            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(SessionLifetime);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(CancellationToken.None);
                }

                SetFailed(
                    session,
                    cancellationToken.IsCancellationRequested
                        ? "GitHub sign-in was canceled."
                        : "GitHub sign-in timed out.");
                return;
            }
            finally
            {
                await Task.WhenAll(standardOutput, standardError);
            }

            SetOutcome(
                session.SessionId,
                process.ExitCode == 0 ? "succeeded" : "failed",
                process.ExitCode == 0
                    ? "GitHub sign-in completed. Retrying the assessment."
                    : "GitHub sign-in did not complete.");
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            SetFailed(session, "Git Credential Manager is unavailable. Install Git for Windows with Git Credential Manager.");
        }
    }

    private void SetFailed(GitHubAuthenticationSession session, string message)
    {
        SetOutcome(session.SessionId, "failed", message);
    }

    private void SetOutcome(string sessionId, string status, string message)
    {
        while (sessions.TryGetValue(sessionId, out var current)
               && current.Status == "pending")
        {
            var updated = current with { Status = status, Message = message };
            if (sessions.TryUpdate(sessionId, updated, current))
            {
                return;
            }
        }
    }

    private void RemoveExpiredSessions()
    {
        foreach (var expired in sessions
                     .Where(item => item.Value.ExpiresAt <= DateTimeOffset.UtcNow)
                     .Select(item => item.Key))
        {
            Cancel(expired);
            sessions.TryRemove(expired, out _);
        }
    }

    private static bool IsValidSessionId(string sessionId) =>
        sessionId.Length == 32 && sessionId.All(Uri.IsHexDigit);
}