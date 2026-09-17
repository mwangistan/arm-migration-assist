using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Validation.BuildValidation;

public interface IRepositoryInspector
{
    Task<RepositoryContext> InspectAsync(RepositoryTarget target, CancellationToken cancellationToken);
}

public static class RepositoryPaths
{
    public static string ResolveWithin(string root, string relative)
    {
        if (Path.IsPathRooted(relative))
            throw new InvalidDataException($"Expected a repository-relative path: {relative}");
        string fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        string path = Path.GetFullPath(Path.Combine(fullRoot, relative));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!path.Equals(fullRoot, comparison) && !path.StartsWith(fullRoot + Path.DirectorySeparatorChar, comparison))
            throw new InvalidDataException($"Path escapes repository: {relative}");
        RejectLinks(path, fullRoot);
        return path;
    }

    public static void RejectLinks(string path, string? stopAt = null)
    {
        for (var current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) &&
                File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidDataException($"Linked paths are not supported: {current}");
            if (current == stopAt) break;
        }
    }

    public static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}

public sealed class GitRepositoryInspector(IProcessRunner processRunner) : IRepositoryInspector
{
    public async Task<RepositoryContext> InspectAsync(RepositoryTarget target, CancellationToken cancellationToken)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(target.Path));
        if (!Directory.Exists(root))
            throw new InvalidDataException($"Repository does not exist: {root}");
        RepositoryPaths.RejectLinks(root);
        if (target.CommitSha is not null && !Regex.IsMatch(target.CommitSha, @"\A(?:[0-9a-fA-F]{40}|[0-9a-fA-F]{64})\z"))
            throw new InvalidDataException("Commit must be a full 40- or 64-character hexadecimal SHA.");

        async Task<string> Git(params string[] arguments)
        {
            var outcome = await processRunner.RunAsync(
                new("git", new[] { "--no-replace-objects", "-c", "core.fsmonitor=false", "-c", "submodule.recurse=false" }
                    .Concat(arguments).ToArray(), root,
                    RunnerEnvironment.Capture(new Dictionary<string, string> { ["GIT_NO_LAZY_FETCH"] = "1" }), 30),
                cancellationToken);
            if (outcome.ExitCode != 0 || outcome.StartError is not null || outcome.TimedOut ||
                outcome.Cancelled || outcome.OutputTruncated || outcome.OutputIncomplete)
                throw new InvalidDataException($"Repository verification failed: git {string.Join(' ', arguments)}: " +
                    $"{outcome.StartError ?? outcome.StandardError}");
            return outcome.StandardOutput.TrimEnd('\r', '\n');
        }

        // Read effective configuration before any index/worktree inspection. Even status can
        // execute clean/process filters; partial clones can lazily invoke external fetch helpers.
        foreach (string key in (await Git("config", "--null", "--name-only", "--list", "--includes")).Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            if (Regex.IsMatch(key, @"\Afilter\..*\.(?:clean|smudge|process)\z", RegexOptions.IgnoreCase | RegexOptions.Singleline))
                throw new InvalidDataException($"Executable Git filter configuration is not supported: {key}");
            if (key.Equals("extensions.partialclone", StringComparison.OrdinalIgnoreCase) ||
                Regex.IsMatch(key, @"\Aremote\..*\.promisor\z", RegexOptions.IgnoreCase | RegexOptions.Singleline))
                throw new InvalidDataException("Partial clones are not supported; inspection must not fetch objects.");
        }

        string top = Path.TrimEndingDirectorySeparator(Path.GetFullPath(await Git("rev-parse", "--show-toplevel")));
        if (!top.Equals(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidDataException("Repository path must be the Git working-tree root, not a subdirectory.");
        string commit = await Git("rev-parse", "--verify", "HEAD");
        if (target.CommitSha is not null && !commit.Equals(target.CommitSha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Commit mismatch: expected {target.CommitSha}, found {commit}.");
        string branchName = await Git("rev-parse", "--abbrev-ref", "HEAD");
        string? branch = branchName == "HEAD" ? null : branchName;
        if (target.Branch is not null && target.Branch != branch)
            throw new InvalidDataException($"Branch mismatch: expected {target.Branch}, found {branch ?? "detached HEAD"}.");
        var tree = new Dictionary<string, (string Mode, string ObjectId)>(StringComparer.Ordinal);
        foreach (string entry in (await Git("ls-tree", "-r", "-z", "--full-tree", commit)).Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            int separator = entry.IndexOf('\t');
            string[] fields = entry[..Math.Max(separator, 0)].Split(' ');
            if (separator < 0 || fields.Length != 3)
                throw new InvalidDataException("Invalid committed tree metadata.");
            if (fields[0] == "160000")
                throw new InvalidDataException("Submodules are not supported for commit-proven validation.");
            if (fields[0] is not ("100644" or "100755") || fields[1] != "blob")
                throw new InvalidDataException("Only regular tracked files are supported for commit-proven validation.");
            tree.Add(entry[(separator + 1)..], (fields[0], fields[2]));
        }

        var indexed = new HashSet<string>(StringComparer.Ordinal);
        foreach (string entry in (await Git("ls-files", "--stage", "-v", "-z")).Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            int separator = entry.IndexOf('\t');
            string[] fields = entry[..Math.Max(separator, 0)].Split(' ');
            if (separator < 0 || fields.Length != 4)
                throw new InvalidDataException("Invalid index metadata.");
            if (fields[0] != "H")
                throw new InvalidDataException("Unsupported index flags: assume-unchanged, skip-worktree and unmerged entries are not allowed.");
            if (fields[1] == "160000")
                throw new InvalidDataException("Submodules are not supported for commit-proven validation.");
            string file = entry[(separator + 1)..];
            if (fields[3] != "0" || !indexed.Add(file) || !tree.TryGetValue(file, out var expected) ||
                expected != (fields[1], fields[2]))
                throw new InvalidDataException($"Index does not match the pinned commit: {file}");
        }
        if (indexed.Count != tree.Count)
            throw new InvalidDataException("Index does not contain the complete pinned commit.");

        // Do not trust Git's stat cache or status: hash every tracked file, not just projects.
        // Raw blob hashing deliberately performs no filters, encoding or newline conversions.
        foreach (var (file, expected) in tree)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path = RepositoryPaths.ResolveWithin(root, file);
            if (!File.Exists(path) || !HashBlob(path, expected.ObjectId.Length).Equals(expected.ObjectId, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Tracked file bytes do not match the pinned commit: {file}");
        }
        if ((await Git("ls-files", "--others", "--exclude-standard", "-z")).Length != 0)
            throw new InvalidDataException("Repository must be clean (including untracked files) to validate the recorded commit.");

        // Only tracked build artifacts are discovery inputs; ignored samples/output are not inferred as projects.
        var files = tree.Keys
            .Where(path => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
                           path.EndsWith(".vcxproj", StringComparison.OrdinalIgnoreCase) ||
                           Path.GetFileName(path) == "Dockerfile")
            .Order(StringComparer.Ordinal).ToArray();
        var artifacts = new List<RepositoryArtifact>();
        var notices = new List<string>();
        foreach (var file in files)
        {
            string path = RepositoryPaths.ResolveWithin(root, file);
            if (new FileInfo(path).Length > 262_144)
            {
                notices.Add($"Discovery skipped oversized build artifact: {file}");
                continue;
            }
            artifacts.Add(new(file, RepositoryPaths.HashFile(path), await File.ReadAllTextAsync(path, cancellationToken)));
        }
        return new(root, commit, branch, artifacts, notices);
    }

    private static string HashBlob(string path, int objectIdLength)
    {
        using var stream = File.OpenRead(path);
        using var hash = IncrementalHash.CreateHash(objectIdLength switch
        {
            40 => HashAlgorithmName.SHA1,
            64 => HashAlgorithmName.SHA256,
            _ => throw new InvalidDataException("Unsupported Git object format.")
        });
        hash.AppendData(Encoding.ASCII.GetBytes($"blob {stream.Length.ToString(CultureInfo.InvariantCulture)}\0"));
        byte[] buffer = new byte[81920];
        int count;
        while ((count = stream.Read(buffer, 0, buffer.Length)) != 0)
            hash.AppendData(buffer, 0, count);
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}
