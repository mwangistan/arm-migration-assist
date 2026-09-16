namespace ArmMigrationAssist.Api.Assessment;

/// <summary>Shared repository file enumeration used by every assessment skill.</summary>
public static class RepoFiles
{
    private static readonly string[] SkipSegments =
    [
        $"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}",
        $"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}",
        $"{Path.DirectorySeparatorChar}.venv{Path.DirectorySeparatorChar}",
        $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
        $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"
    ];

    public static IEnumerable<string> Enumerate(string root)
    {
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            if (SkipSegments.Any(s => path.Contains(s, StringComparison.OrdinalIgnoreCase)))
                continue;
            yield return path;
        }
    }
}
