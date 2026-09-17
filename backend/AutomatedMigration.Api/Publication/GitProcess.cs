using System.Diagnostics;

namespace AutomatedMigration.Api.Publication;

// Thin wrapper around `git` for the operations the publisher needs.
// Kept tiny on purpose: no history parsing, no porcelain scraping.
internal static class GitProcess
{
    public sealed record ProcessResult(int ExitCode, string StdOut, string StdErr)
    {
        public bool Succeeded => ExitCode == 0;
    }

    public static async Task<ProcessResult> RunAsync(
        string workingDirectory,
        IEnumerable<string> arguments,
        TimeSpan timeout,
        IDictionary<string, string>? env,
        CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in arguments) psi.ArgumentList.Add(arg);
        if (env is not null)
        {
            foreach (var kv in env) psi.Environment[kv.Key] = kv.Value;
        }
        // Silence noisy git config that may live on developer machines and stop
        // credential helpers from prompting inside ACA replicas.
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

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        return new ProcessResult(process.ExitCode, stdout, stderr);
    }
}
