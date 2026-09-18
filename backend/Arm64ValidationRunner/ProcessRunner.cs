using System.Diagnostics;
using System.Text;

namespace Arm64ValidationRunner;

public sealed class ProcessResult
{
    public int ExitCode { get; init; }
    public string Stdout { get; init; } = string.Empty;
    public string Stderr { get; init; } = string.Empty;
    public TimeSpan Duration { get; init; }
    public bool TimedOut { get; init; }
}

public static class ProcessRunner
{
    // Executes a command and captures stdout/stderr. Tail-truncated to keep responses reasonable.
    public static async Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        TimeSpan timeout,
        IDictionary<string, string?>? extraEnv = null,
        CancellationToken cancellationToken = default)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
            EnableRaisingEvents = true,
        };
        foreach (var arg in arguments)
        {
            process.StartInfo.ArgumentList.Add(arg);
        }
        if (extraEnv is not null)
        {
            foreach (var (k, v) in extraEnv)
            {
                process.StartInfo.Environment[k] = v;
            }
        }

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (stdout) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (stderr) stderr.AppendLine(e.Data); };

        var sw = Stopwatch.StartNew();
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedCts.CancelAfter(timeout);

        bool timedOut = false;
        try
        {
            await process.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            timedOut = true;
            try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
            try { await process.WaitForExitAsync().ConfigureAwait(false); } catch { /* best effort */ }
        }
        sw.Stop();

        return new ProcessResult
        {
            ExitCode = timedOut ? -1 : process.ExitCode,
            Stdout = TailTruncate(stdout.ToString(), 8_000),
            Stderr = TailTruncate(stderr.ToString(), 8_000),
            Duration = sw.Elapsed,
            TimedOut = timedOut,
        };
    }

    // Keeps the last N bytes so we see the actual failure at the end of long log streams.
    public static string TailTruncate(string text, int maxBytes)
    {
        if (text.Length <= maxBytes) return text;
        var start = text.Length - maxBytes;
        var nl = text.IndexOf('\n', start);
        return "…\n" + (nl >= 0 ? text[(nl + 1)..] : text[start..]);
    }
}
