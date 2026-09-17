using System.IO.Compression;

namespace ArmMigrationAssist.RepositoryDiscovery.GitHub;

internal sealed record ExtractedRepositoryArchive(
    string RootPath,
    IReadOnlyList<string> RelativePaths,
    int TotalFiles,
    int SkippedFiles);

internal static class RepositoryArchiveExtractor
{
    private const int MaximumFiles = 100_000;
    private const long MaximumEntryBytes = 67_108_864;
    private const long MaximumExtractedBytes = 536_870_912;

    public static async Task<ExtractedRepositoryArchive> ExtractAsync(
        string archivePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destinationPath);
        var destinationRoot = Path.GetFullPath(destinationPath);
        var pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var pathComparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var extractedPaths = new HashSet<string>(pathComparer);
        var totalFiles = 0;
        var skippedFiles = 0;
        long extractedBytes = 0;

        await using var archiveStream = new FileStream(
            archivePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            65_536,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read, leaveOpen: false);
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.FullName.EndsWith("/", StringComparison.Ordinal))
            {
                continue;
            }

            totalFiles++;
            var relativePath = GetRepositoryRelativePath(entry.FullName);
            if (relativePath is null
                || IsSymbolicLink(entry)
                || extractedPaths.Count >= MaximumFiles
                || entry.Length < 0
                || entry.Length > MaximumEntryBytes
                || extractedBytes > MaximumExtractedBytes - entry.Length)
            {
                skippedFiles++;
                continue;
            }

            var fullPath = Path.GetFullPath(Path.Combine(
                destinationRoot,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!fullPath.StartsWith(destinationRoot + Path.DirectorySeparatorChar, pathComparison)
                || !extractedPaths.Add(relativePath))
            {
                skippedFiles++;
                continue;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                await using var input = entry.Open();
                await using var output = new FileStream(
                    fullPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    65_536,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                await input.CopyToAsync(output, cancellationToken);
                extractedBytes += entry.Length;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                extractedPaths.Remove(relativePath);
                File.Delete(fullPath);
                skippedFiles++;
            }
        }

        return new ExtractedRepositoryArchive(
            destinationRoot,
            extractedPaths.Order(StringComparer.Ordinal).ToArray(),
            totalFiles,
            skippedFiles);
    }

    private static string? GetRepositoryRelativePath(string archivePath)
    {
        var normalized = archivePath.Replace('\\', '/');
        var rootSeparator = normalized.IndexOf('/');
        if (rootSeparator <= 0 || rootSeparator == normalized.Length - 1)
        {
            return null;
        }

        var relativePath = normalized[(rootSeparator + 1)..];
        if (relativePath.Length > 1024
            || Path.IsPathRooted(relativePath)
            || relativePath.Split('/').Any(segment => segment is "" or "." or ".."))
        {
            return null;
        }

        return relativePath;
    }

    private static bool IsSymbolicLink(ZipArchiveEntry entry) =>
        ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000;
}