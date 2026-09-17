namespace ArmMigrationAssist.Api.Assessment.RepositoryDiscovery;

using System.Text.Json;
using System.Xml.Linq;

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
        [".rs"] = "Rust", [".go"] = "Go", [".java"] = "Java", [".kt"] = "Kotlin", [".kts"] = "Kotlin", [".scala"] = "Scala",
        [".py"] = "Python", [".js"] = "JavaScript", [".mjs"] = "JavaScript", [".cjs"] = "JavaScript",
        [".ts"] = "TypeScript", [".tsx"] = "TypeScript", [".jsx"] = "JavaScript",
        [".rb"] = "Ruby", [".php"] = "PHP", [".swift"] = "Swift", [".m"] = "Objective-C", [".mm"] = "Objective-C++",
        [".asm"] = "Assembly", [".s"] = "Assembly",
        [".dart"] = "Dart", [".lua"] = "Lua", [".pl"] = "Perl", [".pm"] = "Perl", [".sql"] = "SQL",
        [".razor"] = "Razor", [".cshtml"] = "Razor", [".vue"] = "Vue", [".svelte"] = "Svelte",
        [".sh"] = "Shell", [".bash"] = "Shell", [".ps1"] = "PowerShell", [".psm1"] = "PowerShell",
        [".bat"] = "Batch", [".cmd"] = "Batch",
        [".r"] = "R", [".jl"] = "Julia", [".groovy"] = "Groovy", [".hs"] = "Haskell", [".zig"] = "Zig",
        [".ex"] = "Elixir", [".exs"] = "Elixir", [".clj"] = "Clojure"
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

            if (LanguageByExtension.TryGetValue(ext, out var lang) && !IsVendoredOrGenerated(rel))
            {
                long size = SafeLength(path);
                var cur = langBytes.GetValueOrDefault(lang);
                langBytes[lang] = (cur.count + 1, cur.bytes + size);
            }

            switch (name.ToLowerInvariant())
            {
                case "cmakelists.txt": buildSystems.Add("CMake"); projectFiles.Add(rel); break;
                case "makefile": case "gnumakefile": buildSystems.Add("Make"); break;
                case "build.ninja": buildSystems.Add("Ninja"); break;
                case "meson.build": buildSystems.Add("Meson"); break;
                case "sconstruct": buildSystems.Add("SCons"); break;
                case "build.xml": buildSystems.Add("Ant"); break;
                case "build": case "build.bazel": case "workspace": case "workspace.bazel": case "module.bazel":
                    buildSystems.Add("Bazel"); break;
                case "pom.xml": buildSystems.Add("Maven"); packageManagers.Add("Maven"); projectFiles.Add(rel); break;
                case "build.gradle": case "build.gradle.kts": case "settings.gradle": case "settings.gradle.kts":
                    buildSystems.Add("Gradle"); packageManagers.Add("Gradle"); projectFiles.Add(rel); break;
                case "setup.py": case "setup.cfg": buildSystems.Add("setuptools"); packageManagers.Add("pip"); manifestPaths.Add(path); break;
                case "webpack.config.js": case "webpack.config.ts": case "webpack.config.cjs": case "webpack.config.mjs":
                    buildSystems.Add("Webpack"); break;
                case "vite.config.js": case "vite.config.ts": case "vite.config.cjs": case "vite.config.mjs":
                    buildSystems.Add("Vite"); break;
                case "rollup.config.js": case "rollup.config.ts": case "rollup.config.mjs":
                    buildSystems.Add("Rollup"); break;
                case "package.json": packageManagers.Add("npm"); projectFiles.Add(rel); manifestPaths.Add(path); break;
                case "yarn.lock": packageManagers.Add("yarn"); break;
                case "pnpm-lock.yaml": packageManagers.Add("pnpm"); break;
                case "requirements.txt": packageManagers.Add("pip"); manifestPaths.Add(path); break;
                case "pyproject.toml": packageManagers.Add("pip"); projectFiles.Add(rel); manifestPaths.Add(path); break;
                case "pipfile": packageManagers.Add("pipenv"); break;
                case "vcpkg.json": packageManagers.Add("vcpkg"); projectFiles.Add(rel); break;
                case "conanfile.txt": case "conanfile.py": packageManagers.Add("conan"); break;
                case "packages.config": packageManagers.Add("NuGet"); break;
                case "cargo.toml": buildSystems.Add("Cargo"); packageManagers.Add("crates.io"); projectFiles.Add(rel); break;
                case "go.mod": buildSystems.Add("Go modules"); packageManagers.Add("Go modules"); projectFiles.Add(rel); break;
                case "composer.json": packageManagers.Add("composer"); break;
                case "gemfile": packageManagers.Add("bundler"); break;
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

        AnalyzeManifests(manifestPaths, frameworks, projectTypes, buildSystems, packageManagers);
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

    private static void AnalyzeManifests(
        List<string> manifestPaths,
        HashSet<string> frameworks,
        HashSet<string> projectTypes,
        HashSet<string> buildSystems,
        HashSet<string> packageManagers)
    {
        foreach (var path in manifestPaths)
        {
            string text;
            try { text = File.ReadAllText(path); } catch { continue; }
            var name = Path.GetFileName(path).ToLowerInvariant();

            if (name == "package.json")
                AnalyzePackageJson(text, frameworks, projectTypes, buildSystems);
            else if (name.EndsWith(".csproj"))
                AnalyzeCsproj(text, frameworks, projectTypes);
            else if (name == "pyproject.toml")
                AnalyzePyproject(text, frameworks, projectTypes, buildSystems, packageManagers);
            else // requirements.txt / setup.py / setup.cfg
                DetectPythonFrameworks(PythonDependencyNames(text), frameworks, projectTypes);
        }
    }

    /// <summary>
    /// Structured package.json analysis: parse JSON and match exact dependency keys (across
    /// dependencies/devDependencies/peer/optional) so a package merely containing "react" in its
    /// name no longer produces a false positive.
    /// </summary>
    private static void AnalyzePackageJson(
        string text, HashSet<string> frameworks, HashSet<string> projectTypes, HashSet<string> buildSystems)
    {
        var deps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var doc = JsonDocument.Parse(text);
            foreach (var section in new[] { "dependencies", "devDependencies", "peerDependencies", "optionalDependencies" })
            {
                if (doc.RootElement.TryGetProperty(section, out var obj) && obj.ValueKind == JsonValueKind.Object)
                    foreach (var prop in obj.EnumerateObject())
                        deps.Add(prop.Name);
            }
        }
        catch (JsonException)
        {
            return; // malformed manifest - skip rather than guess
        }

        if (deps.Contains("react")) frameworks.Add("react");
        if (deps.Contains("vue")) frameworks.Add("vue");
        if (deps.Contains("@angular/core")) frameworks.Add("angular");
        if (deps.Contains("svelte")) frameworks.Add("svelte");
        if (deps.Contains("next")) frameworks.Add("next.js");
        if (deps.Contains("electron")) { frameworks.Add("electron"); projectTypes.Add("desktop"); }
        if (deps.Contains("@tauri-apps/api") || deps.Contains("@tauri-apps/cli")) { frameworks.Add("tauri"); projectTypes.Add("desktop"); }
        if (deps.Contains("express") || deps.Contains("koa") || deps.Contains("fastify") || deps.Contains("@nestjs/core"))
            projectTypes.Add("service");

        if (deps.Contains("webpack")) buildSystems.Add("Webpack");
        if (deps.Contains("vite")) buildSystems.Add("Vite");
        if (deps.Contains("rollup")) buildSystems.Add("Rollup");
        if (deps.Contains("esbuild")) buildSystems.Add("esbuild");
        if (deps.Contains("parcel") || deps.Contains("@parcel/core")) buildSystems.Add("Parcel");
    }

    /// <summary>
    /// Structured .csproj analysis via XML, tolerant of whitespace and element casing
    /// (e.g. both &lt;UseWPF&gt;true and &lt;usewpf&gt; true match).
    /// </summary>
    private static void AnalyzeCsproj(string text, HashSet<string> frameworks, HashSet<string> projectTypes)
    {
        XDocument xml;
        try { xml = XDocument.Parse(text); } catch (System.Xml.XmlException) { return; }

        bool Flag(string element) => xml.Descendants()
            .Any(e => string.Equals(e.Name.LocalName, element, StringComparison.OrdinalIgnoreCase)
                      && bool.TryParse(e.Value.Trim(), out var b) && b);

        string? PropText(string element) => xml.Descendants()
            .FirstOrDefault(e => string.Equals(e.Name.LocalName, element, StringComparison.OrdinalIgnoreCase))
            ?.Value.Trim();

        var sdk = xml.Root?.Attribute("Sdk")?.Value ?? string.Empty;
        var packageRefs = xml.Descendants()
            .Where(e => string.Equals(e.Name.LocalName, "PackageReference", StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Attribute("Include")?.Value ?? string.Empty)
            .ToList();

        if (Flag("UseWPF")) { frameworks.Add("wpf"); projectTypes.Add("desktop"); }
        if (Flag("UseWindowsForms")) { frameworks.Add("winforms"); projectTypes.Add("desktop"); }
        if (Flag("UseMaui")) { frameworks.Add("maui"); projectTypes.Add("desktop"); }
        if (sdk.Contains("Microsoft.NET.Sdk.Web", StringComparison.OrdinalIgnoreCase))
            { frameworks.Add("aspnetcore"); projectTypes.Add("service"); }
        if (packageRefs.Any(r => r.Contains("Microsoft.WindowsAppSDK", StringComparison.OrdinalIgnoreCase)
                              || r.Contains("Microsoft.UI.Xaml", StringComparison.OrdinalIgnoreCase)))
            { frameworks.Add("winui3"); projectTypes.Add("desktop"); }

        var outputType = PropText("OutputType");
        if (string.Equals(outputType, "Library", StringComparison.OrdinalIgnoreCase)) projectTypes.Add("library");
        if (string.Equals(outputType, "Exe", StringComparison.OrdinalIgnoreCase)
            || string.Equals(outputType, "WinExe", StringComparison.OrdinalIgnoreCase)) projectTypes.Add("cli");
    }

    /// <summary>
    /// pyproject.toml analysis: derive the PEP 517 build backend (setuptools, Hatchling, Flit,
    /// maturin, PDM, Poetry, ...) and detect Python frameworks from declared dependencies.
    /// </summary>
    private static void AnalyzePyproject(
        string text, HashSet<string> frameworks, HashSet<string> projectTypes,
        HashSet<string> buildSystems, HashSet<string> packageManagers)
    {
        var backend = ParseBuildBackend(text);
        if (backend is not null) buildSystems.Add(backend);
        if (backend == "Poetry") packageManagers.Add("poetry");
        if (backend == "PDM") packageManagers.Add("pdm");

        DetectPythonFrameworks(PythonDependencyNames(text), frameworks, projectTypes);
    }

    private static string? ParseBuildBackend(string pyprojectText)
    {
        foreach (var raw in pyprojectText.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith("build-backend", StringComparison.OrdinalIgnoreCase)) continue;
            var idx = line.IndexOf('=');
            if (idx < 0) continue;
            var value = line[(idx + 1)..].Trim().Trim('"', '\'').ToLowerInvariant();

            if (value.StartsWith("setuptools")) return "setuptools";
            if (value.StartsWith("hatchling")) return "Hatchling";
            if (value.StartsWith("flit_core") || value.StartsWith("flit")) return "Flit";
            if (value.StartsWith("maturin")) return "maturin";
            if (value.StartsWith("pdm")) return "PDM";
            if (value.StartsWith("poetry")) return "Poetry";
            if (value.StartsWith("scikit_build_core") || value.StartsWith("scikit-build")) return "scikit-build";
            if (value.StartsWith("mesonpy")) return "meson-python";
            return null;
        }
        return null;
    }

    /// <summary>Extracts bare package names from requirements.txt / setup.cfg / pyproject dependency lines.</summary>
    private static IEnumerable<string> PythonDependencyNames(string text)
    {
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            line = line.Trim('"', '\'', ',', '[', ']');
            var name = new string(line.TakeWhile(c =>
                char.IsLetterOrDigit(c) || c is '-' or '_' or '.').ToArray());
            if (name.Length > 0) yield return name.ToLowerInvariant();
        }
    }

    private static void DetectPythonFrameworks(
        IEnumerable<string> depNames, HashSet<string> frameworks, HashSet<string> projectTypes)
    {
        var deps = new HashSet<string>(depNames, StringComparer.OrdinalIgnoreCase);
        if (deps.Contains("torch")) frameworks.Add("pytorch");
        if (deps.Contains("tensorflow")) frameworks.Add("tensorflow");
        if (deps.Any(d => d is "pyqt5" or "pyqt6" or "pyside2" or "pyside6")) { frameworks.Add("qt"); projectTypes.Add("desktop"); }
        if (deps.Contains("kivy") || deps.Contains("wxpython")) projectTypes.Add("desktop");
        if (deps.Contains("flask") || deps.Contains("fastapi") || deps.Contains("django")) projectTypes.Add("service");
    }

    private static readonly string[] VendorSegments =
        ["vendor", "vendored", "third_party", "thirdparty", "external", "externals", "dist", "build", "out", "target"];

    /// <summary>
    /// Gap 4: keep vendored and generated files out of the language byte tally so the primary
    /// language is not skewed by checked-in dependencies, bundled output, or minified assets.
    /// </summary>
    internal static bool IsVendoredOrGenerated(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(seg => VendorSegments.Contains(seg, StringComparer.OrdinalIgnoreCase)))
            return true;

        var file = segments.Length > 0 ? segments[^1] : normalized;
        return file.EndsWith(".min.js", StringComparison.OrdinalIgnoreCase)
            || file.EndsWith(".min.css", StringComparison.OrdinalIgnoreCase)
            || file.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
            || file.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase)
            || file.EndsWith(".pb.go", StringComparison.OrdinalIgnoreCase)
            || file.EndsWith("_pb2.py", StringComparison.OrdinalIgnoreCase);
    }

    private static long SafeLength(string path)
    {
        try { return new FileInfo(path).Length; } catch { return 0; }
    }
}
