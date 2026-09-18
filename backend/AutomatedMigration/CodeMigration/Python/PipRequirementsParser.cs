using System.Text.RegularExpressions;

namespace AutomatedMigration.CodeMigration.Python;

// Static, offline parser for pip package specifiers. Reads requirements.txt and
// pyproject.toml [project.dependencies]/[project.optional-dependencies] and
// emits a normalized list. No index queries; the audit runners never touch PyPI.
internal static class PipRequirementsParser
{
    public sealed record PipDependency(
        string Name,
        string NormalizedName,
        string? VersionSpec,
        string SourceFile,
        int LineNumber);

    // Matches the head of a PEP 508 requirement: `name[extras] <ver-spec>`.
    // We only need the name (any word chars, dot, dash, underscore) and the raw
    // version spec (everything after the name until ';' or end-of-line).
    private static readonly Regex Req = new(
        @"^\s*(?<name>[A-Za-z0-9][A-Za-z0-9._-]*)(?:\[[^\]]*\])?\s*(?<spec>(?:==|>=|<=|~=|!=|<|>)[^;#]+)?",
        RegexOptions.Compiled);

    public static IReadOnlyList<PipDependency> ScanRepo(string repoPath)
    {
        var deps = new List<PipDependency>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // requirements*.txt anywhere in the tree (comfyui ships requirements.txt at root)
        foreach (var path in EnumerateSafe(repoPath, "requirements*.txt", SearchOption.AllDirectories))
        {
            ParseRequirementsTxt(repoPath, path, deps, seen);
        }

        var pyproject = Path.Combine(repoPath, "pyproject.toml");
        if (File.Exists(pyproject))
        {
            ParsePyprojectToml(repoPath, pyproject, deps, seen);
        }
        return deps;
    }

    private static IEnumerable<string> EnumerateSafe(string root, string pattern, SearchOption option)
    {
        try
        {
            return Directory.EnumerateFiles(root, pattern, option);
        }
        catch (Exception)
        {
            return Array.Empty<string>();
        }
    }

    private static void ParseRequirementsTxt(
        string repoPath, string path, List<PipDependency> deps, HashSet<string> seen)
    {
        var relative = Path.GetRelativePath(repoPath, path).Replace('\\', '/');
        var lines = File.ReadAllLines(path);
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith('-'))
                continue;
            var m = Req.Match(line);
            if (!m.Success)
                continue;
            var name = m.Groups["name"].Value;
            var normalized = Normalize(name);
            if (!seen.Add(normalized))
                continue;
            var spec = m.Groups["spec"].Success ? m.Groups["spec"].Value.Trim() : null;
            deps.Add(new PipDependency(name, normalized, spec, relative, i + 1));
        }
    }

    private static readonly Regex PyprojectDep = new(
        "^\\s*\"(?<line>[^\"]+)\"", RegexOptions.Compiled);

    private static void ParsePyprojectToml(
        string repoPath, string path, List<PipDependency> deps, HashSet<string> seen)
    {
        var relative = Path.GetRelativePath(repoPath, path).Replace('\\', '/');
        var lines = File.ReadAllLines(path);
        var inDeps = false;
        for (var i = 0; i < lines.Length; i++)
        {
            var raw = lines[i];
            var trimmed = raw.TrimStart();
            if (trimmed.StartsWith("dependencies", StringComparison.OrdinalIgnoreCase)
                && trimmed.Contains('='))
            {
                inDeps = true;
                continue;
            }
            if (inDeps)
            {
                if (trimmed.StartsWith(']'))
                {
                    inDeps = false;
                    continue;
                }
                var m = PyprojectDep.Match(raw);
                if (!m.Success)
                    continue;
                var inner = m.Groups["line"].Value.Trim();
                var reqMatch = Req.Match(inner);
                if (!reqMatch.Success)
                    continue;
                var name = reqMatch.Groups["name"].Value;
                var normalized = Normalize(name);
                if (!seen.Add(normalized))
                    continue;
                var spec = reqMatch.Groups["spec"].Success ? reqMatch.Groups["spec"].Value.Trim() : null;
                deps.Add(new PipDependency(name, normalized, spec, relative, i + 1));
            }
        }
    }

    public static string Normalize(string name) =>
        name.ToLowerInvariant().Replace('_', '-').Replace('.', '-');
}
