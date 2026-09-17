using System.Diagnostics;
using System.Text;

namespace ArmMigrationAssist.RepositoryDiscovery;

internal sealed class GitClient(bool useStoredGitHubCredentials = false)
{
    public async Task<string> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var result = await TryRunAsync(workingDirectory, arguments, cancellationToken);
        if (result.ExitCode != 0)
        {
            if (IsAuthenticationFailure(result.StandardError))
            {
                throw new RepositoryAuthenticationRequiredException(
                    "GitHub authentication is required or repository access could not be verified.");
            }

            throw new RepositoryDiscoveryException("Git could not inspect the repository.");
        }

        return result.StandardOutput;
    }

    public async Task<GitResult> TryRunAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = CreateStartInfo(workingDirectory, arguments),
        };

        try
        {
            process.Start();
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            throw new RepositoryDiscoveryException("Git is required but could not be started.", exception);
        }

        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }

                await process.WaitForExitAsync(CancellationToken.None);
            }
            catch (InvalidOperationException)
            {
                // The process exited between the state check and termination.
            }

            await Task.WhenAll(standardOutput, standardError);
            throw;
        }

        return new GitResult(
            process.ExitCode,
            await standardOutput,
            await standardError);
    }

    private ProcessStartInfo CreateStartInfo(
        string workingDirectory,
        IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            StandardErrorEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8,
            UseShellExecute = false,
        };

        startInfo.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        startInfo.Environment["GIT_CONFIG_GLOBAL"] = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        startInfo.Environment["GCM_INTERACTIVE"] = "Never";
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(useStoredGitHubCredentials
            ? "credential.helper=manager"
            : "credential.helper=");
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add($"core.hooksPath={(OperatingSystem.IsWindows() ? "NUL" : "/dev/null")}");
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("submodule.recurse=false");
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static bool IsAuthenticationFailure(string standardError) =>
        standardError.Contains("authentication failed", StringComparison.OrdinalIgnoreCase)
        || standardError.Contains("could not read Username", StringComparison.OrdinalIgnoreCase)
        || standardError.Contains("terminal prompts disabled", StringComparison.OrdinalIgnoreCase)
        || standardError.Contains("repository not found", StringComparison.OrdinalIgnoreCase)
        || standardError.Contains("returned error: 401", StringComparison.OrdinalIgnoreCase)
        || standardError.Contains("returned error: 403", StringComparison.OrdinalIgnoreCase)
        || standardError.Contains("access denied", StringComparison.OrdinalIgnoreCase);
}

internal sealed record GitResult(int ExitCode, string StandardOutput, string StandardError);

public class RepositoryDiscoveryException : Exception
{
    public RepositoryDiscoveryException(string message)
        : base(message)
    {
    }

    public RepositoryDiscoveryException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class RepositoryAuthenticationRequiredException : RepositoryDiscoveryException
{
    public RepositoryAuthenticationRequiredException(string message)
        : base(message)
    {
    }
}