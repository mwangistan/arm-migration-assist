namespace Arm64ValidationRunner;

public enum ToolchainKind { Unknown, Dotnet, Python, Node }

public sealed record DetectedToolchain(ToolchainKind Kind, string? PrimaryEntry);

public static class ToolchainDetector
{
    public static DetectedToolchain Detect(string repoRoot, ProjectHints? hints)
    {
        if (hints is not null)
        {
            if (hints.DotnetProjectPaths is { Count: > 0 })
                return new DetectedToolchain(ToolchainKind.Dotnet, Resolve(repoRoot, hints.DotnetProjectPaths[0]));
            if (hints.PythonRequirementsPaths is { Count: > 0 })
                return new DetectedToolchain(ToolchainKind.Python, Resolve(repoRoot, hints.PythonRequirementsPaths[0]));
            if (hints.NodeManifestPaths is { Count: > 0 })
                return new DetectedToolchain(ToolchainKind.Node, Path.GetDirectoryName(Resolve(repoRoot, hints.NodeManifestPaths[0])) ?? repoRoot);

            if (!string.IsNullOrWhiteSpace(hints.PrimaryLanguage))
            {
                var lang = hints.PrimaryLanguage.ToLowerInvariant();
                if (lang.Contains("python")) return AutoDetectPython(repoRoot);
                if (lang.Contains(".net") || lang.Contains("csharp") || lang.Contains("c#")) return AutoDetectDotnet(repoRoot);
                if (lang.Contains("node") || lang.Contains("typescript") || lang.Contains("javascript")) return AutoDetectNode(repoRoot);
            }
        }

        var dotnet = AutoDetectDotnet(repoRoot);
        if (dotnet.Kind != ToolchainKind.Unknown) return dotnet;
        var python = AutoDetectPython(repoRoot);
        if (python.Kind != ToolchainKind.Unknown) return python;
        var node = AutoDetectNode(repoRoot);
        return node;
    }

    private static DetectedToolchain AutoDetectDotnet(string root)
    {
        var sln = FindFirst(root, "*.sln");
        if (sln is not null) return new DetectedToolchain(ToolchainKind.Dotnet, sln);
        var slnx = FindFirst(root, "*.slnx");
        if (slnx is not null) return new DetectedToolchain(ToolchainKind.Dotnet, slnx);
        var csproj = FindFirst(root, "*.csproj");
        if (csproj is not null) return new DetectedToolchain(ToolchainKind.Dotnet, csproj);
        return new DetectedToolchain(ToolchainKind.Unknown, null);
    }

    private static DetectedToolchain AutoDetectPython(string root)
    {
        var req = Path.Combine(root, "requirements.txt");
        if (File.Exists(req)) return new DetectedToolchain(ToolchainKind.Python, req);
        var pyproject = Path.Combine(root, "pyproject.toml");
        if (File.Exists(pyproject)) return new DetectedToolchain(ToolchainKind.Python, pyproject);
        var setup = Path.Combine(root, "setup.py");
        if (File.Exists(setup)) return new DetectedToolchain(ToolchainKind.Python, setup);
        return new DetectedToolchain(ToolchainKind.Unknown, null);
    }

    private static DetectedToolchain AutoDetectNode(string root)
    {
        var pkg = Path.Combine(root, "package.json");
        if (File.Exists(pkg)) return new DetectedToolchain(ToolchainKind.Node, root);
        return new DetectedToolchain(ToolchainKind.Unknown, null);
    }

    private static string? FindFirst(string root, string pattern)
    {
        try
        {
            return Directory.EnumerateFiles(root, pattern, SearchOption.TopDirectoryOnly).FirstOrDefault()
                ?? Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories)
                    .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}"))
                    .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}"))
                    .OrderBy(p => p.Length)
                    .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static string Resolve(string root, string relative)
    {
        var combined = Path.Combine(root, relative);
        return Path.GetFullPath(combined);
    }
}
