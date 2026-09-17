namespace ArmMigrationAssist.RepositoryWorkspace;

public sealed record GitProcessResult(int ExitCode, string StdOut, string StdErr)
{
    public bool Succeeded => ExitCode == 0;
}

public interface IGitProcess
{
    Task<GitProcessResult> RunAsync(string workingDirectory, IReadOnlyList<string> arguments, CancellationToken cancellationToken);
    Task<GitProcessResult> RunAsync(string workingDirectory, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken);
}
