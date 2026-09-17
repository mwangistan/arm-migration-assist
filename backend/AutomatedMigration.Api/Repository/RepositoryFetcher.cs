using System.IO.Compression;
using System.Net.Http.Headers;

namespace AutomatedMigration.Api.Repository;

// Slim shallow-fetch of a specific commit from GitHub, mirroring the safe
// extraction shape used by Feature 1 (bounded archive size, no symlinks, no
// path traversal). Does not use `git`; only the zipball REST endpoint. Future
// PR should factor this and F1's copy into a shared library.
public sealed class RepositoryFetcher
{
    private const long MaxArchiveBytes = 268_435_456;      // 256 MiB
    private const long MaxEntryBytes = 67_108_864;         // 64 MiB
    private const long MaxExtractedBytes = 536_870_912;    // 512 MiB
    private const int MaxFiles = 100_000;

    private readonly HttpClient _http;

    public RepositoryFetcher(HttpClient http)
    {
        _http = http;
        if (_http.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            _http.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("arm-migration-assist-automation-api", "0.1.0"));
        }
        if (_http.DefaultRequestHeaders.Accept.Count == 0)
        {
            _http.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        }
    }

    public async Task<FetchedRepository> FetchAsync(
        string repositoryUrl,
        string commitSha,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(repositoryUrl, UriKind.Absolute, out var uri) ||
            !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Only https://github.com/<owner>/<repo> URLs are supported.", nameof(repositoryUrl));
        }
        var segments = uri.AbsolutePath.Trim('/').Split('/', 3);
        if (segments.Length < 2)
        {
            throw new ArgumentException("Repository URL is missing owner/name segments.", nameof(repositoryUrl));
        }
        if (!IsHex(commitSha) || commitSha.Length < 40)
        {
            throw new ArgumentException("commitSha must be a full 40-char SHA.", nameof(commitSha));
        }

        var owner = Uri.EscapeDataString(segments[0]);
        var repo = Uri.EscapeDataString(segments[1]);
        var zipUrl = $"https://api.github.com/repos/{owner}/{repo}/zipball/{commitSha}";

        var scratch = Path.Combine(Path.GetTempPath(), "amma-fetch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);

        var archivePath = Path.Combine(scratch, "repository.zip");
        var extractPath = Path.Combine(scratch, "repository");

        try
        {
            await DownloadAsync(zipUrl, archivePath, cancellationToken);
            var extracted = await ExtractAsync(archivePath, extractPath, cancellationToken);
            File.Delete(archivePath);
            return new FetchedRepository(extracted.Root, scratch, extracted.TotalFiles, extracted.SkippedFiles);
        }
        catch
        {
            TryDeleteDirectory(scratch);
            throw;
        }
    }

    private async Task DownloadAsync(string url, string destination, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var contentLength = response.Content.Headers.ContentLength ?? -1;
        if (contentLength > MaxArchiveBytes)
        {
            throw new InvalidOperationException(
                $"Archive exceeds the {MaxArchiveBytes} byte cap ({contentLength} bytes).");
        }
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var buffer = new byte[81920];
        long copied = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            copied += read;
            if (copied > MaxArchiveBytes)
            {
                throw new InvalidOperationException(
                    $"Archive stream exceeded the {MaxArchiveBytes} byte cap while downloading.");
            }
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    private static async Task<Extraction> ExtractAsync(
        string archivePath, string destinationPath, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destinationPath);
        var destRoot = Path.GetFullPath(destinationPath);
        var totalFiles = 0;
        var skipped = 0;
        long extractedBytes = 0;
        string? rootDir = null;

        await using var stream = File.OpenRead(archivePath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.FullName.EndsWith("/", StringComparison.Ordinal))
            {
                continue;
            }
            totalFiles++;
            var segments = entry.FullName.Split('/', 2);
            if (segments.Length != 2)
            {
                skipped++;
                continue;
            }
            rootDir ??= segments[0];
            if (segments[0] != rootDir ||
                entry.Length < 0 ||
                entry.Length > MaxEntryBytes ||
                extractedBytes > MaxExtractedBytes - entry.Length ||
                (uint)(entry.ExternalAttributes >> 16) == 0xA000 || // symlink
                totalFiles > MaxFiles)
            {
                skipped++;
                continue;
            }
            var relative = segments[1];
            if (relative.Contains("..", StringComparison.Ordinal))
            {
                skipped++;
                continue;
            }
            var full = Path.GetFullPath(Path.Combine(destRoot, relative));
            if (!full.StartsWith(destRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
                full != destRoot)
            {
                skipped++;
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            await using var entryStream = entry.Open();
            await using var target = new FileStream(full, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await entryStream.CopyToAsync(target, cancellationToken);
            extractedBytes += entry.Length;
        }
        return new Extraction(destRoot, totalFiles, skipped);
    }

    private static bool IsHex(string s) =>
        s.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'));

    internal static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
            // best-effort cleanup
        }
    }

    private sealed record Extraction(string Root, int TotalFiles, int SkippedFiles);
}

public sealed record FetchedRepository(string Root, string ScratchDir, int TotalFiles, int SkippedFiles) : IDisposable
{
    public void Dispose() => RepositoryFetcher.TryDeleteDirectory(ScratchDir);
}
