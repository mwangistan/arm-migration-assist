using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Win32.SafeHandles;
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
        string? setsid = null;
        if (!OperatingSystem.IsWindows())
        {
            setsid = ResolveExecutable("setsid", invocation.Environment);
            if (setsid is null)
                return new(started, DateTimeOffset.UtcNow, null, "", "",
                    StartError: "Cannot safely run commands because setsid is unavailable.");
            if (ResolveExecutable(invocation.Executable, invocation.Environment, invocation.WorkingDirectory) is null)
                return new(started, DateTimeOffset.UtcNow, null, "", "",
                    StartError: $"Executable not found: {invocation.Executable}");
        }
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = setsid ?? invocation.Executable,
                WorkingDirectory = invocation.WorkingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        if (setsid is not null)
            process.StartInfo.ArgumentList.Add(invocation.Executable);
        foreach (var argument in invocation.Arguments)
            process.StartInfo.ArgumentList.Add(argument);
        process.StartInfo.Environment.Clear();
        foreach (var (name, value) in invocation.Environment)
            process.StartInfo.Environment[name] = value;

        WindowsProcessJob? job = null;
        try
        {
            if (OperatingSystem.IsWindows())
                job = WindowsProcessJob.Create();
            process.Start();
            job?.Assign(process);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            job?.Dispose();
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (Exception cleanupError) when (cleanupError is Win32Exception or InvalidOperationException) { }
            return new(started, DateTimeOffset.UtcNow, null, "", "", StartError: ex.Message);
        }

        using (job)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(invocation.TimeoutSeconds));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken);
            var stdout = ReadBoundedAsync(process.StandardOutput, linked.Token);
            var stderr = ReadBoundedAsync(process.StandardError, linked.Token);
            bool timedOut = false, cancelled = false;
            bool cleanupIncomplete = false;
            try
            {
                await process.WaitForExitAsync(linked.Token);
                cleanupIncomplete |= !TerminateProcessLifetime(process, job);
                await Task.WhenAll(stdout, stderr).WaitAsync(linked.Token);
            }
            catch (OperationCanceledException)
            {
                cancelled = cancellationToken.IsCancellationRequested;
                timedOut = !cancelled;
                cleanupIncomplete |= !TerminateProcessLifetime(process, job);
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
    }

    private static string? ResolveExecutable(string executable, IReadOnlyDictionary<string, string> environment,
        string? workingDirectory = null)
    {
        if (executable.Contains(Path.DirectorySeparatorChar) || executable.Contains(Path.AltDirectorySeparatorChar))
        {
            string path = Path.IsPathFullyQualified(executable)
                ? executable
                : Path.GetFullPath(executable, workingDirectory ?? Directory.GetCurrentDirectory());
            return File.Exists(path) ? path : null;
        }
        if (!environment.TryGetValue("PATH", out string? pathValue))
            return null;
        return pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(path => Path.Combine(path, executable))
            .FirstOrDefault(File.Exists);
    }

    private static bool TerminateProcessLifetime(Process process, WindowsProcessJob? job)
    {
        if (job is not null)
            return job.Terminate();
        int result = kill(-process.Id, 9);
        return result == 0 || Marshal.GetLastWin32Error() == 3;
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

    [DllImport("libc", SetLastError = true)]
    private static extern int kill(int pid, int signal);

    private sealed class WindowsProcessJob : SafeHandleZeroOrMinusOneIsInvalid
    {
        private const uint KillOnJobClose = 0x00002000;

        private WindowsProcessJob() : base(ownsHandle: true) { }

        public static WindowsProcessJob Create()
        {
            var job = CreateJobObject(IntPtr.Zero, null);
            if (job.IsInvalid)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            var information = new JobObjectExtendedLimitInformation
            {
                BasicLimitInformation = new JobObjectBasicLimitInformation { LimitFlags = KillOnJobClose }
            };
            if (!SetInformationJobObject(job, 9, ref information, (uint)Marshal.SizeOf<JobObjectExtendedLimitInformation>()))
            {
                int error = Marshal.GetLastWin32Error();
                job.Dispose();
                throw new Win32Exception(error);
            }
            return job;
        }

        public void Assign(Process process)
        {
            if (!AssignProcessToJobObject(this, process.Handle))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        public bool Terminate() => TerminateJobObject(this, 1);

        protected override bool ReleaseHandle() => CloseHandle(handle);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern WindowsProcessJob CreateJobObject(IntPtr jobAttributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(WindowsProcessJob job, int informationClass,
        ref JobObjectExtendedLimitInformation information, uint informationLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(WindowsProcessJob job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateJobObject(WindowsProcessJob job, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
