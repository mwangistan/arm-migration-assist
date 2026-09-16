namespace ArmMigrationAssist.Api.Assessment.RepositoryDiscovery;

/// <summary>
/// Story 1.2 - Technology Stack Discovery (assessment skill).
/// Walks the cloned repository and identifies languages, project files, build systems,
/// package managers, entry points, and whether CI already exists.
/// </summary>
public sealed class TechnologyStackDiscoverySkill : IAssessmentSkill
{
    public string Name => "technology-discovery";
    public int Order => 10;
    public string Description => "Detects languages, frameworks, project types, build systems, package managers, installers and CI from the repository tree.";
    public IReadOnlyList<string> Outputs => ["technology"];

    private static readonly Dictionary<string, string> LanguageByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".cs"] = "C#", [".vb"] = "VB.NET", [".fs"] = "F#",
        [".c"] = "C", [".h"] = "C/C++ Header", [".cpp"] = "C++", [".cc"] = "C++", [".cxx"] = "C++", [".hpp"] = "C++ Header",
        [".rs"] = "Rust", [".go"] = "Go", [".java"] = "Java", [".kt"] = "Kotlin",
        [".py"] = "Python", [".js"] = "JavaScript", [".mjs"] = "JavaScript", [".cjs"] = "JavaScript",
        [".ts"] = "TypeScript", [".tsx"] = "TypeScript", [".jsx"] = "JavaScript",
        [".rb"] = "Ruby", [".php"] = "PHP", [".swift"] = "Swift", [".m"] = "Objective-C",
        [".asm"] = "Assembly", [".s"] = "Assembly"
    };

    public Task ContributeAsync(RepositorySnapshot repo, ReadinessManifest manifest, CancellationToken ct = default)
    {
        manifest.Technology = Discover(repo);
        return Task.CompletedTask;
    }

    public TechnologyProfile Discover(RepositorySnapshot snapshot)
    {
        var root = snapshot.LocalRepoPath;
        var langBytes = new Dictionary<string, (int count, long bytes)>(StringComparer.OrdinalIgnoreCase);
        var buildSystems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var packageManagers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var projectFiles = new List<string>();
        var entryPoints = new List<string>();
        var installers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ciSystems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var frameworks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var projectTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var manifestPaths = new List<string>();
        bool hasCi = false;

        foreach (var path in RepoFiles.Enumerate(root))
        {
            var rel = Path.GetRelativePath(root, path);
            var name = Path.GetFileName(path);
            var ext = Path.GetExtension(path);

            if (LanguageByExtension.TryGetValue(ext, out var lang))
            {
                long size = SafeLength(path);
                var cur = langBytes.GetValueOrDefault(lang);
                langBytes[lang] = (cur.count + 1, cur.bytes + size);
            }

            switch (name.ToLowerInvariant())
            {
                case "cmakelists.txt": buildSystems.Add("CMake"); projectFiles.Add(rel); break;
                case "makefile": buildSystems.Add("Make"); break;
                case "package.json": packageManagers.Add("npm"); projectFiles.Add(rel); manifestPaths.Add(path); break;
                case "requirements.txt": packageManagers.Add("pip"); manifestPaths.Add(path); break;
                case "pyproject.toml": packageManagers.Add("pip/poetry"); projectFiles.Add(rel); manifestPaths.Add(path); break;
                case "vcpkg.json": packageManagers.Add("vcpkg"); projectFiles.Add(rel); break;
                case "packages.config": packageManagers.Add("NuGet"); break;
                case "cargo.toml": buildSystems.Add("Cargo"); packageManagers.Add("crates.io"); projectFiles.Add(rel); break;
                case "go.mod": buildSystems.Add("Go modules"); projectFiles.Add(rel); break;
                case "appxmanifest.xml": case "package.appxmanifest": installers.Add("msix"); break;
            }

            switch (ext.ToLowerInvariant())
            {
                case ".sln": buildSystems.Add("MSBuild"); projectFiles.Add(rel); break;
                case ".csproj": buildSystems.Add("MSBuild"); packageManagers.Add("NuGet"); projectFiles.Add(rel); manifestPaths.Add(path); break;
                case ".vcxproj": buildSystems.Add("MSBuild (C++)"); projectFiles.Add(rel); break;
                case ".fsproj": case ".vbproj": buildSystems.Add("MSBuild"); projectFiles.Add(rel); break;
                case ".wxs": installers.Add("wix"); break;
                case ".iss": installers.Add("inno-setup"); break;
                case ".nsi": installers.Add("nsis"); break;
                case ".msix": case ".appx": installers.Add("msix"); break;
                case ".msi": installers.Add("msi"); break;
            }

            if (rel.Replace('\\', '/').StartsWith(".github/workflows/", StringComparison.OrdinalIgnoreCase)
                && (ext.Equals(".yml", StringComparison.OrdinalIgnoreCase) || ext.Equals(".yaml", StringComparison.OrdinalIgnoreCase)))
            { hasCi = true; ciSystems.Add("github-actions"); }
            if (name.Equals("azure-pipelines.yml", StringComparison.OrdinalIgnoreCase))
            { hasCi = true; ciSystems.Add("azure-pipelines"); }
            if (name.Equals("appveyor.yml", StringComparison.OrdinalIgnoreCase))
            { hasCi = true; ciSystems.Add("appveyor"); }

            if (ext.Equals(".xaml", StringComparison.OrdinalIgnoreCase)) frameworks.Add("xaml");

            if (name is "main.py" or "app.py" or "Program.cs" or "main.go" or "main.rs" or "index.js" or "server.js" or "main.cpp" or "main.c")
                entryPoints.Add(rel);
        }

        DetectFrameworks(manifestPaths, frameworks, projectTypes);
        if (entryPoints.Count > 0 && projectTypes.Count == 0) projectTypes.Add("application");

        var languages = langBytes
            .Select(kv => new LanguageStat(kv.Key, kv.Value.count, kv.Value.bytes))
            .OrderByDescending(l => l.Bytes)
            .ToList();

        return new TechnologyProfile
        {
            Languages = languages,
            Frameworks = [.. frameworks.OrderBy(x => x)],
            ProjectTypes = [.. projectTypes.OrderBy(x => x)],
            BuildSystems = [.. buildSystems.OrderBy(x => x)],
            PackageManagers = [.. packageManagers.OrderBy(x => x)],
            Installers = [.. installers.OrderBy(x => x)],
            ProjectFiles = projectFiles.OrderBy(x => x).Take(200).ToList(),
            EntryPoints = entryPoints,
            HasExistingCi = hasCi,
            CiSystems = [.. ciSystems.OrderBy(x => x)]
        };
    }

    private static void DetectFrameworks(List<string> manifestPaths, HashSet<string> frameworks, HashSet<string> projectTypes)
    {
        foreach (var path in manifestPaths)
        {
            string text;
            try { text = File.ReadAllText(path); } catch { continue; }
            var lower = text.ToLowerInvariant();
            var name = Path.GetFileName(path).ToLowerInvariant();

            if (name == "package.json")
            {
                if (lower.Contains("\"electron\"")) { frameworks.Add("electron"); projectTypes.Add("desktop"); }
                if (lower.Contains("\"react\"")) frameworks.Add("react");
                if (lower.Contains("\"vue\"")) frameworks.Add("vue");
                if (lower.Contains("\"@angular/core\"")) frameworks.Add("angular");
                if (lower.Contains("\"tauri\"")) { frameworks.Add("tauri"); projectTypes.Add("desktop"); }
            }
            else if (name.EndsWith(".csproj"))
            {
                if (lower.Contains("<usewpf>true")) { frameworks.Add("wpf"); projectTypes.Add("desktop"); }
                if (lower.Contains("<usewindowsforms>true")) { frameworks.Add("winforms"); projectTypes.Add("desktop"); }
                if (lower.Contains("microsoft.net.sdk.web")) { frameworks.Add("aspnetcore"); projectTypes.Add("service"); }
                if (lower.Contains("microsoft.windowsappsdk") || lower.Contains("winui")) { frameworks.Add("winui3"); projectTypes.Add("desktop"); }
                if (lower.Contains("<outputtype>library")) projectTypes.Add("library");
                if (lower.Contains("<outputtype>exe")) projectTypes.Add("cli");
            }
            else // requirements.txt / pyproject.toml
            {
                if (lower.Contains("torch")) frameworks.Add("pytorch");
                if (lower.Contains("tensorflow")) frameworks.Add("tensorflow");
                if (lower.Contains("pyqt") || lower.Contains("pyside")) { frameworks.Add("qt"); projectTypes.Add("desktop"); }
                if (lower.Contains("flask") || lower.Contains("fastapi") || lower.Contains("django")) projectTypes.Add("service");
            }
        }
    }

    private static long SafeLength(string path)
    {
        try { return new FileInfo(path).Length; } catch { return 0; }
    }
}
