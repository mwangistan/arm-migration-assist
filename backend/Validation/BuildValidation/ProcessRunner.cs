using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Validation.BuildValidation;

public interface IProcessRunner
{
    Task<ProcessOutcome> RunAsync(ProcessInvocation invocation, CancellationToken cancellationToken);
}

public static class RunnerEnvironment
{
    // Inherit only toolchain/OS essentials, not arbitrary session credentials.
    private static readonly string[] InheritedNames =
    [
        "PATH", "SystemRoot", "WINDIR", "COMSPEC", "TEMP", "TMP", "HOME", "USERPROFILE",
        "APPDATA", "LOCALAPPDATA", "ProgramFiles", "ProgramFiles(x86)", "ProgramW6432",
        "DOTNET_ROOT", "DOTNET_ROOT(x86)", "NUGET_PACKAGES", "VSINSTALLDIR", "VCINSTALLDIR",
        "INCLUDE", "LIB", "LIBPATH", "VCToolsInstallDir", "WindowsSdkDir",
        "GIT_CONFIG_GLOBAL", "GIT_CONFIG_NOSYSTEM"
    ];

    public static RunnerContext Current => new(
        OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsLinux() ? "linux" : "other",
        RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant(), Environment.MachineName);

    public static IReadOnlyDictionary<string, string> Capture(IReadOnlyDictionary<string, string> overrides)
    {
        var result = new Dictionary<string, string>(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var name in InheritedNames)
            if (Environment.GetEnvironmentVariable(name) is { } value)
                result[name] = value;
        foreach (var (key, value) in overrides)
            result[key] = value;
        return result;
    }

    public static ProcessInvocation Redact(ProcessInvocation invocation) => invocation with
    {
        Environment = invocation.Environment.ToDictionary(pair => pair.Key,
            pair => IsSensitive(pair.Key) ? "[REDACTED]" : pair.Value)
    };

    private static bool IsSensitive(string key) =>
        new[] { "TOKEN", "PASSWORD", "SECRET", "KEY", "CONNECTION", "CREDENTIAL" }
            .Any(part => key.Contains(part, StringComparison.OrdinalIgnoreCase));
}

public sealed class LocalProcessRunner : IProcessRunner
{
    private const int MaxOutputCharacters = 1_048_576;

    public async Task<ProcessOutcome> RunAsync(ProcessInvocation invocation, CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        if (cancellationToken.IsCancellationRequested)
            return new(started, DateTimeOffset.UtcNow, null, "", "", Cancelled: true);
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = invocation.Executable,
                WorkingDirectory = invocation.WorkingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        foreach (var argument in invocation.Arguments)
            process.StartInfo.ArgumentList.Add(argument);
        process.StartInfo.Environment.Clear();
        foreach (var (name, value) in invocation.Environment)
            process.StartInfo.Environment[name] = value;

        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            return new(started, DateTimeOffset.UtcNow, null, "", "", StartError: ex.Message);
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(invocation.TimeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken);
        var stdout = ReadBoundedAsync(process.StandardOutput, linked.Token);
        var stderr = ReadBoundedAsync(process.StandardError, linked.Token);
        bool timedOut = false, cancelled = false;
        bool cleanupIncomplete = false;
        try
        {
            await process.WaitForExitAsync(linked.Token);
            // A descendant may inherit the pipes after the main process exits. The same deadline
            // applies to draining output, not just to waiting for the original process.
            await Task.WhenAll(stdout, stderr).WaitAsync(linked.Token);
        }
        catch (OperationCanceledException)
        {
            cancelled = cancellationToken.IsCancellationRequested;
            timedOut = !cancelled;
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            catch (Win32Exception) { cleanupIncomplete = true; }
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (TimeoutException) { cleanupIncomplete = true; }
        }
        var output = await stdout;
        var error = await stderr;
        cancelled |= cancellationToken.IsCancellationRequested;
        timedOut |= timeout.IsCancellationRequested && !cancelled;
        return new(started, DateTimeOffset.UtcNow, process.HasExited ? process.ExitCode : null, output.Text, error.Text,
            TimedOut: timedOut, Cancelled: cancelled, OutputTruncated: output.Truncated || error.Truncated,
            OutputIncomplete: cleanupIncomplete || output.Incomplete || error.Incomplete);
    }

    private static async Task<(string Text, bool Truncated, bool Incomplete)> ReadBoundedAsync(
        StreamReader reader, CancellationToken cancellationToken)
    {
        var buffer = new char[4096];
        var text = new StringBuilder();
        bool truncated = false;
        try
        {
            int count;
            while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
            {
                int retained = Math.Min(count, MaxOutputCharacters - text.Length);
                text.Append(buffer, 0, retained);
                truncated |= retained < count;
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException)
        {
            return (text.ToString(), truncated, true);
        }
        return (text.ToString(), truncated, false);
    }
}
