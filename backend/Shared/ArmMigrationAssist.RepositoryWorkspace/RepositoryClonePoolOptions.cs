namespace ArmMigrationAssist.RepositoryWorkspace;

public sealed class RepositoryClonePoolOptions
{
    public const string SectionName = "RepositoryClonePool";

    // Directory that will hold cached clones. Defaults to a stable subdirectory under
    // %TEMP% so multiple app instances on the same machine don't fight for the same paths.
    public string? RootDirectory { get; set; }

    // Hard timeout for a single `git clone` or `git fetch` invocation.
    public int GitOperationTimeoutSeconds { get; set; } = 300;

    // Path to the git binary. Left null in production so PATH resolution wins.
    public string GitExecutable { get; set; } = "git";
}
