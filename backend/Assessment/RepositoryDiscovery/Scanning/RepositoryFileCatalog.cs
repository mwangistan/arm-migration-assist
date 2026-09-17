using System.Text;

namespace ArmMigrationAssist.RepositoryDiscovery.Scanning;

internal sealed record RepositoryFile(
    string RelativePath,
    string FullPath,
    string FileName,
    string Extension,
    string? Content);

internal sealed record RepositoryFileCatalog(
    IReadOnlyList<RepositoryFile> Files,
    int TotalFiles,
    int SkippedFiles)
{
    private const int MaximumFiles = 100_000;
    private const int MaximumFileBytes = 1_048_576;
    private const int MaximumTotalContentBytes = 33_554_432;

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".appxmanifest", ".asm", ".c", ".cc", ".cjs", ".cpp", ".cs", ".csproj", ".css",
        ".fs", ".fsproj", ".go", ".gradle", ".h", ".hpp", ".html", ".iss", ".java", ".js",
        ".json", ".jsx", ".kt", ".m", ".mjs", ".msixproj", ".nsi", ".props", ".py", ".razor",
        ".rb", ".rs", ".s", ".sh", ".sln", ".svelte", ".targets", ".toml", ".ts", ".tsx",
        ".vb", ".vbproj", ".vcxproj", ".vue", ".wixproj", ".wxs", ".xml", ".xaml", ".yaml", ".yml",
    };

    private static readonly HashSet<string> TextFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CMakeLists.txt", "Dockerfile", "Gemfile", "LICENSE", "Makefile", "NOTICE", "Pipfile",
        "packages.config", "requirements.txt",
    };

    public static async Task<RepositoryFileCatalog> CreateAsync(
        string rootPath,
        GitClient git,
        CancellationToken cancellationToken,
        IReadOnlyList<string>? knownRelativePaths = null,
        int? knownTotalFiles = null,
        int knownSkippedFiles = 0)
    {
        var entries = knownRelativePaths is null
            ? ParseEntries(await git.RunAsync(
                rootPath,
                ["ls-files", "--stage", "-z"],
                cancellationToken))
            : knownRelativePaths.Select(path => new GitEntry("100644", path)).ToList();
        var files = new List<RepositoryFile>(Math.Min(entries.Count, MaximumFiles));
        var skipped = knownSkippedFiles;
        var contentBytes = 0;

        foreach (var entry in entries.OrderBy(entry => entry.Path, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (files.Count >= MaximumFiles || entry.Mode is "120000" or "160000")
            {
                skipped++;
                continue;
            }

            if (!TryResolveSafePath(rootPath, entry.Path, out var fullPath)
                || !File.Exists(fullPath))
            {
                skipped++;
                continue;
            }

            var fileInfo = new FileInfo(fullPath);
            string? content = null;
            var isTextCandidate = IsTextCandidate(fileInfo.Name, fileInfo.Extension);
            var canReadContent = isTextCandidate
                && fileInfo.Length <= MaximumFileBytes
                && contentBytes + fileInfo.Length <= MaximumTotalContentBytes;
            if (canReadContent)
            {
                content = await ReadTextAsync(fullPath, cancellationToken);
                if (content is not null)
                {
                    contentBytes += checked((int)fileInfo.Length);
                }
            }

            if (isTextCandidate && content is null)
            {
                skipped++;
            }

            files.Add(new RepositoryFile(
                entry.Path.Replace('\\', '/'),
                fullPath,
                fileInfo.Name,
                fileInfo.Extension.ToLowerInvariant(),
                content));
        }

        return new RepositoryFileCatalog(files, knownTotalFiles ?? entries.Count, skipped);
    }

    private static List<GitEntry> ParseEntries(string output)
    {
        var entries = new List<GitEntry>();
        foreach (var record in output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var tabIndex = record.IndexOf('\t');
            var spaceIndex = record.IndexOf(' ');
            if (tabIndex <= 0 || spaceIndex <= 0 || spaceIndex > tabIndex)
            {
                continue;
            }

            entries.Add(new GitEntry(record[..spaceIndex], record[(tabIndex + 1)..]));
        }

        return entries;
    }

    private static bool TryResolveSafePath(string rootPath, string relativePath, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(relativePath)
            || relativePath.Length > 1024
            || Path.IsPathRooted(relativePath)
            || relativePath.Split('/', '\\').Any(segment => segment is ".." or "."))
        {
            return false;
        }

        var root = Path.GetFullPath(rootPath);
        var candidate = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!candidate.StartsWith(root + Path.DirectorySeparatorChar, comparison))
        {
            return false;
        }

        var current = root;
        foreach (var segment in relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(current);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return false;
            }

            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                return false;
            }
        }

        fullPath = candidate;
        return true;
    }

    private static bool IsTextCandidate(string fileName, string extension) =>
        TextExtensions.Contains(extension) || TextFileNames.Contains(fileName);

    private static async Task<string?> ReadTextAsync(string path, CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        if (bytes.Contains((byte)0))
        {
            return null;
        }

        return Encoding.UTF8.GetString(bytes);
    }

    private sealed record GitEntry(string Mode, string Path);
}