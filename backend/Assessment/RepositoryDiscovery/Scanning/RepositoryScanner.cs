using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using ArmMigrationAssist.RepositoryDiscovery.Models;

namespace ArmMigrationAssist.RepositoryDiscovery.Scanning;

internal sealed partial class RepositoryScanner
{
    private static readonly IReadOnlyDictionary<string, string> LanguageByExtension =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".c"] = "c",
            [".cc"] = "cpp",
            [".cpp"] = "cpp",
            [".cxx"] = "cpp",
            [".cs"] = "csharp",
            [".dart"] = "dart",
            [".fs"] = "fsharp",
            [".go"] = "go",
            [".java"] = "java",
            [".js"] = "javascript",
            [".jsx"] = "javascript",
            [".kt"] = "kotlin",
            [".php"] = "php",
            [".ps1"] = "powershell",
            [".py"] = "python",
            [".rb"] = "ruby",
            [".rs"] = "rust",
            [".sh"] = "shell",
            [".swift"] = "swift",
            [".ts"] = "typescript",
            [".tsx"] = "typescript",
            [".vb"] = "visual-basic",
        };

    private static readonly HashSet<string> SourceCodeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".c", ".cc", ".cpp", ".cs", ".css", ".fs", ".go", ".h", ".hpp", ".html", ".java",
        ".js", ".jsx", ".kt", ".py", ".razor", ".rb", ".rs", ".svelte", ".swift", ".ts",
        ".tsx", ".vb", ".vue", ".xaml",
    };

    public DiscoveryScan Scan(RepositoryFileCatalog catalog)
    {
        var technology = ScanTechnology(catalog.Files);
        var build = ScanBuild(catalog.Files);
        var windows = ScanWindowsExperience(catalog.Files, technology);

        return new DiscoveryScan(
            technology.Inventory,
            build,
            windows.Experience,
            windows.OfflineCapabilityEstablished,
            DetectLicense(catalog.Files));
    }

    private static TechnologyScan ScanTechnology(IReadOnlyList<RepositoryFile> files)
    {
        var languages = new HashSet<string>(StringComparer.Ordinal);
        var frameworks = new HashSet<string>(StringComparer.Ordinal);
        var projectTypes = new HashSet<string>(StringComparer.Ordinal);
        var buildSystems = new HashSet<string>(StringComparer.Ordinal);
        var packageManagers = new HashSet<string>(StringComparer.Ordinal);
        var installers = new HashSet<string>(StringComparer.Ordinal);
        var ciSystems = new HashSet<string>(StringComparer.Ordinal);
        var frameworkSources = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var file in files)
        {
            var path = file.RelativePath;
            var lowerPath = path.ToLowerInvariant();
            if (LanguageByExtension.TryGetValue(file.Extension, out var language))
            {
                languages.Add(language);
            }

            DetectBuildSystemAndPackageManager(file, lowerPath, buildSystems, packageManagers);
            DetectCiSystem(lowerPath, ciSystems);
            DetectInstaller(file, lowerPath, installers);
            DetectProjectFile(file, frameworks, projectTypes, frameworkSources);
            DetectPackageJson(file, frameworks, projectTypes, frameworkSources);
            DetectTextFrameworks(file, frameworks, projectTypes, frameworkSources);
        }

        if (projectTypes.Count == 0 && languages.Count > 0)
        {
            projectTypes.Add("unknown");
        }

        return new TechnologyScan(
            new TechnologyInventory(
                Sorted(languages),
                Sorted(frameworks),
                Sorted(projectTypes),
                Sorted(buildSystems),
                Sorted(packageManagers),
                Sorted(installers),
                Sorted(ciSystems)),
            frameworkSources);
    }

    private static void DetectBuildSystemAndPackageManager(
        RepositoryFile file,
        string lowerPath,
        ISet<string> buildSystems,
        ISet<string> packageManagers)
    {
        if (file.Extension is ".csproj" or ".fsproj" or ".vbproj" or ".vcxproj"
            || file.Extension.Equals(".sln", StringComparison.OrdinalIgnoreCase))
        {
            buildSystems.Add("msbuild");
        }

        if (file.Extension is ".csproj" or ".fsproj" or ".vbproj"
            || lowerPath.EndsWith("packages.config", StringComparison.Ordinal)
            || lowerPath.EndsWith("packages.lock.json", StringComparison.Ordinal))
        {
            packageManagers.Add("nuget");
        }

        if (file.FileName.Equals("CMakeLists.txt", StringComparison.OrdinalIgnoreCase))
        {
            buildSystems.Add("cmake");
        }

        if (file.FileName.Equals("Makefile", StringComparison.OrdinalIgnoreCase))
        {
            buildSystems.Add("make");
        }

        if (file.FileName.Equals("Dockerfile", StringComparison.OrdinalIgnoreCase)
            || lowerPath.EndsWith(".dockerfile", StringComparison.Ordinal))
        {
            buildSystems.Add("docker");
        }

        if (lowerPath.EndsWith("package.json", StringComparison.Ordinal))
        {
            packageManagers.Add("npm");
        }

        if (lowerPath.EndsWith("pnpm-lock.yaml", StringComparison.Ordinal))
        {
            packageManagers.Add("pnpm");
        }

        if (lowerPath.EndsWith("yarn.lock", StringComparison.Ordinal))
        {
            packageManagers.Add("yarn");
        }

        if (lowerPath.EndsWith("pyproject.toml", StringComparison.Ordinal)
            || lowerPath.EndsWith("requirements.txt", StringComparison.Ordinal)
            || lowerPath.EndsWith("pipfile", StringComparison.Ordinal))
        {
            packageManagers.Add("pip");
        }

        if (lowerPath.EndsWith("pyproject.toml", StringComparison.Ordinal)
            && Contains(file.Content, "[build-system]"))
        {
            buildSystems.Add("pep517");
        }

        if (lowerPath.EndsWith("vcpkg.json", StringComparison.Ordinal)) packageManagers.Add("vcpkg");
        if (lowerPath.EndsWith("conanfile.txt", StringComparison.Ordinal) || lowerPath.EndsWith("conanfile.py", StringComparison.Ordinal)) packageManagers.Add("conan");
        if (lowerPath.EndsWith("cargo.toml", StringComparison.Ordinal)) packageManagers.Add("cargo");
        if (lowerPath.EndsWith("go.mod", StringComparison.Ordinal)) packageManagers.Add("go-modules");
        if (lowerPath.EndsWith("pom.xml", StringComparison.Ordinal))
        {
            packageManagers.Add("maven");
            buildSystems.Add("maven");
        }

        if (lowerPath.EndsWith("build.gradle", StringComparison.Ordinal)
            || lowerPath.EndsWith("build.gradle.kts", StringComparison.Ordinal))
        {
            packageManagers.Add("gradle");
            buildSystems.Add("gradle");
        }
    }

    private static void DetectCiSystem(string lowerPath, ISet<string> ciSystems)
    {
        if (lowerPath.StartsWith(".github/workflows/", StringComparison.Ordinal)
            && (lowerPath.EndsWith(".yml", StringComparison.Ordinal) || lowerPath.EndsWith(".yaml", StringComparison.Ordinal)))
        {
            ciSystems.Add("github-actions");
        }

        if (lowerPath.EndsWith("azure-pipelines.yml", StringComparison.Ordinal)
            || lowerPath.EndsWith("azure-pipelines.yaml", StringComparison.Ordinal))
        {
            ciSystems.Add("azure-pipelines");
        }

        if (lowerPath.EndsWith("appveyor.yml", StringComparison.Ordinal)
            || lowerPath.EndsWith("appveyor.yaml", StringComparison.Ordinal))
        {
            ciSystems.Add("appveyor");
        }

        if (lowerPath.EndsWith(".gitlab-ci.yml", StringComparison.Ordinal))
        {
            ciSystems.Add("gitlab-ci");
        }
    }

    private static void DetectInstaller(RepositoryFile file, string lowerPath, ISet<string> installers)
    {
        if (file.Extension.Equals(".wixproj", StringComparison.OrdinalIgnoreCase)
            || file.Extension.Equals(".wxs", StringComparison.OrdinalIgnoreCase))
        {
            installers.Add("wix");
        }

        if (lowerPath.EndsWith("package.appxmanifest", StringComparison.Ordinal)
            || file.Extension.Equals(".msixproj", StringComparison.OrdinalIgnoreCase))
        {
            installers.Add("msix");
        }

        if (file.Extension.Equals(".iss", StringComparison.OrdinalIgnoreCase))
        {
            installers.Add("inno-setup");
        }

        if (lowerPath.EndsWith("package.json", StringComparison.Ordinal)
            && Contains(file.Content, "squirrel.windows"))
        {
            installers.Add("squirrel");
        }
    }

    private static void DetectProjectFile(
        RepositoryFile file,
        ISet<string> frameworks,
        ISet<string> projectTypes,
        IDictionary<string, string> sources)
    {
        if (file.Content is null
            || file.Extension is not (".csproj" or ".fsproj" or ".vbproj" or ".vcxproj"))
        {
            return;
        }

        var document = TryParseXml(file.Content);
        if (document is null)
        {
            return;
        }

        var sdk = document.Root?.Attribute("Sdk")?.Value ?? string.Empty;
        if (sdk.Contains("Microsoft.NET.Sdk.Web", StringComparison.OrdinalIgnoreCase))
        {
            AddFramework("aspnet-core", file.RelativePath, frameworks, sources);
            projectTypes.Add("web");
        }

        var properties = document.Descendants()
            .GroupBy(element => element.Name.LocalName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.OrdinalIgnoreCase);
        if (IsTrue(properties, "UseWPF")) AddFramework("wpf", file.RelativePath, frameworks, sources);
        if (IsTrue(properties, "UseWindowsForms")) AddFramework("winforms", file.RelativePath, frameworks, sources);
        if (Contains(file.Content, "Microsoft.WindowsAppSDK") || Contains(file.Content, "Microsoft.UI.Xaml")) AddFramework("winui3", file.RelativePath, frameworks, sources);
        if (Contains(file.Content, "Microsoft.Maui")) AddFramework("maui", file.RelativePath, frameworks, sources);

        if (properties.TryGetValue("OutputType", out var outputType))
        {
            if (outputType.Equals("WinExe", StringComparison.OrdinalIgnoreCase)) projectTypes.Add("desktop");
            else if (outputType.Equals("Exe", StringComparison.OrdinalIgnoreCase)) projectTypes.Add("cli");
            else projectTypes.Add("library");
        }
        else if (!sdk.Contains("Web", StringComparison.OrdinalIgnoreCase))
        {
            projectTypes.Add("library");
        }
    }

    private static void DetectPackageJson(
        RepositoryFile file,
        ISet<string> frameworks,
        ISet<string> projectTypes,
        IDictionary<string, string> sources)
    {
        if (!file.RelativePath.EndsWith("package.json", StringComparison.OrdinalIgnoreCase)
            || file.Content is null)
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(file.Content, new JsonDocumentOptions { MaxDepth = 32 });
            foreach (var sectionName in new[] { "dependencies", "devDependencies", "peerDependencies" })
            {
                if (!document.RootElement.TryGetProperty(sectionName, out var section)
                    || section.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                foreach (var dependency in section.EnumerateObject())
                {
                    switch (dependency.Name)
                    {
                        case "electron":
                            AddFramework("electron", file.RelativePath, frameworks, sources);
                            projectTypes.Add("desktop");
                            break;
                        case "react": AddFramework("react", file.RelativePath, frameworks, sources); break;
                        case "next": AddFramework("nextjs", file.RelativePath, frameworks, sources); projectTypes.Add("web"); break;
                        case "vue": AddFramework("vue", file.RelativePath, frameworks, sources); break;
                        case "@angular/core": AddFramework("angular", file.RelativePath, frameworks, sources); break;
                        case "@tauri-apps/api": AddFramework("tauri", file.RelativePath, frameworks, sources); projectTypes.Add("desktop"); break;
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Malformed manifests are left for a manifest-specific scanner.
        }
    }

    private static void DetectTextFrameworks(
        RepositoryFile file,
        ISet<string> frameworks,
        ISet<string> projectTypes,
        IDictionary<string, string> sources)
    {
        if (file.Content is null)
        {
            return;
        }

        if (Contains(file.Content, "find_package(Qt") || Contains(file.Content, "PySide6") || Contains(file.Content, "PyQt6")) AddFramework("qt", file.RelativePath, frameworks, sources);
        if (Contains(file.Content, "torch") && (file.FileName.Equals("pyproject.toml", StringComparison.OrdinalIgnoreCase) || file.FileName.Equals("requirements.txt", StringComparison.OrdinalIgnoreCase))) AddFramework("pytorch", file.RelativePath, frameworks, sources);
        if (Contains(file.Content, "Django") && file.Extension.Equals(".py", StringComparison.OrdinalIgnoreCase)) AddFramework("django", file.RelativePath, frameworks, sources);
        if (Contains(file.Content, "from flask") && file.Extension.Equals(".py", StringComparison.OrdinalIgnoreCase)) AddFramework("flask", file.RelativePath, frameworks, sources);
        if (file.Extension.Equals(".html", StringComparison.OrdinalIgnoreCase))
        {
            AddFramework("web", file.RelativePath, frameworks, sources);
            projectTypes.Add("web");
        }
    }

    private static BuildFindings ScanBuild(IReadOnlyList<RepositoryFile> files)
    {
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var evidence = new List<Evidence>();
        var arm64Target = false;
        var arm64EcTarget = false;
        var arm64Ci = false;
        var packagingArm64 = false;
        RepositoryFile? testFile = null;

        foreach (var file in files)
        {
            var isBuildConfiguration = IsBuildConfiguration(file);
            var isCi = IsCiConfiguration(file.RelativePath);
            var isPackaging = IsPackagingConfiguration(file);
            if ((isBuildConfiguration || isCi || isPackaging) && file.Content is not null)
            {
                var fileTargets = ExtractTargets(file.Content);
                foreach (var target in fileTargets) targets.Add(target);

                var fileHasArm64Ec = fileTargets.Any(IsArm64Ec);
                var fileHasArm64 = fileTargets.Any(target => IsArm64(target) && !IsArm64Ec(target));
                arm64Target |= isBuildConfiguration && fileHasArm64;
                arm64EcTarget |= isBuildConfiguration && fileHasArm64Ec;
                arm64Ci |= isCi && (fileHasArm64 || fileHasArm64Ec);
                packagingArm64 |= isPackaging && (fileHasArm64 || fileHasArm64Ec);

                if (fileTargets.Count > 0)
                {
                    var displayedTargets = fileTargets.Take(10).ToArray();
                    var suffix = fileTargets.Count > displayedTargets.Length
                        ? $" and {fileTargets.Count - displayedTargets.Length} more"
                        : string.Empty;
                    evidence.Add(FileEvidence(
                        file,
                        $"Detected architecture target(s): {string.Join(", ", displayedTargets)}{suffix}."));
                }
            }

            testFile ??= IsTestFile(file) ? file : null;
        }

        if (testFile is not null)
        {
            evidence.Add(FileEvidence(testFile, "Detected an automated test project or test file."));
        }

        if (evidence.Count == 0)
        {
            evidence.Add(new Evidence(
                "artifact",
                null,
                "tracked-file-inventory",
                $"Inspected {files.Count} tracked file(s) for build, CI, packaging, and test signals."));
        }

        var limitedEvidence = DistinctEvidence(evidence);
        return new BuildFindings(
            EvidenceId("build", limitedEvidence),
            arm64Target,
            arm64EcTarget,
            arm64Ci,
            packagingArm64,
            testFile is not null,
            Sorted(targets).Take(500).ToArray(),
            limitedEvidence);
    }

    private static WindowsScan ScanWindowsExperience(
        IReadOnlyList<RepositoryFile> files,
        TechnologyScan technology)
    {
        var evidence = new List<Evidence>();
        var windowsFile = files.FirstOrDefault(file =>
            file.Extension.Equals(".vcxproj", StringComparison.OrdinalIgnoreCase)
            || IsBuildConfiguration(file)
            && (Contains(file.Content, "-windows")
                || Contains(file.Content, "win-arm64")
                || Contains(file.Content, "win-x64")
                || Contains(file.Content, "WindowsTargetPlatformVersion")));
        var windowsVersionExists = windowsFile is not null || technology.Inventory.Installers.Count > 0;
        if (windowsFile is not null)
        {
            evidence.Add(FileEvidence(windowsFile, "Detected an explicit Windows build target or Windows project configuration."));
        }

        var uiTechnology = SelectUiTechnology(technology.Inventory.Frameworks, technology.Inventory.ProjectTypes);
        if (technology.FrameworkSources.TryGetValue(uiTechnology, out var uiPath))
        {
            var uiFile = files.First(file => file.RelativePath == uiPath);
            evidence.Add(FileEvidence(uiFile, $"Detected the {uiTechnology} UI technology."));
        }

        var installerFile = files.FirstOrDefault(IsInstallerFile);
        if (installerFile is not null)
        {
            evidence.Add(FileEvidence(installerFile, "Detected Windows installer or package configuration."));
        }

        var offlineFile = files.FirstOrDefault(file =>
            IsSourceCodeFile(file)
            && (Contains(file.Content, "serviceWorker.register")
                || Contains(file.Content, "caches.open(")));
        if (offlineFile is not null)
        {
            evidence.Add(FileEvidence(offlineFile, "Detected an application-managed offline cache or service worker."));
        }

        var accessibilityFile = files.FirstOrDefault(file =>
            IsSourceCodeFile(file)
            && (Contains(file.Content, "aria-")
                || Contains(file.Content, "AutomationProperties.")
                || Contains(file.Content, "AccessibleName")
                || Contains(file.Content, "accessibilityLabel")));
        if (accessibilityFile is not null)
        {
            evidence.Add(FileEvidence(accessibilityFile, "Detected an accessibility annotation or automation property."));
        }

        var notificationsFile = files.FirstOrDefault(file =>
            IsSourceCodeFile(file)
            && (Contains(file.Content, "ToastNotificationManager")
                || Contains(file.Content, "AppNotificationManager")
                || Contains(file.Content, "Windows.UI.Notifications")));
        if (notificationsFile is not null)
        {
            evidence.Add(FileEvidence(notificationsFile, "Detected Windows notification API integration."));
        }

        var lifecycleFile = files.FirstOrDefault(file =>
            IsSourceCodeFile(file)
            && (Contains(file.Content, "EnteredBackground")
                || Contains(file.Content, "LeavingBackground")
                || Contains(file.Content, "Suspending +=")
                || Contains(file.Content, "Resuming +=")));
        if (lifecycleFile is not null)
        {
            evidence.Add(FileEvidence(lifecycleFile, "Detected Windows application lifecycle event integration."));
        }

        if (evidence.Count == 0)
        {
            evidence.Add(new Evidence(
                "artifact",
                null,
                "tracked-file-inventory",
                $"Inspected {files.Count} tracked file(s) for Windows experience signals."));
        }

        return new WindowsScan(
            new WindowsExperience(
                windowsVersionExists,
                uiTechnology,
                installerFile is not null,
                offlineFile is not null,
                accessibilityFile is null ? "unknown" : "partial",
                accessibilityFile is null ? null : "Static discovery found at least one accessibility annotation.",
                notificationsFile is not null,
                lifecycleFile is not null,
                DistinctEvidence(evidence)),
            offlineFile is not null);
    }

    private static string? DetectLicense(IReadOnlyList<RepositoryFile> files)
    {
        foreach (var file in files.Where(file =>
                     file.FileName.StartsWith("LICENSE", StringComparison.OrdinalIgnoreCase)
                     || file.FileName.StartsWith("COPYING", StringComparison.OrdinalIgnoreCase)
                     || file.RelativePath.EndsWith("package.json", StringComparison.OrdinalIgnoreCase)))
        {
            if (file.Content is null) continue;
            if (Contains(file.Content, "MIT License") || ContainsJsonLicense(file.Content, "MIT")) return "MIT";
            if (Contains(file.Content, "Apache License") && Contains(file.Content, "Version 2.0")) return "Apache-2.0";
            if (Contains(file.Content, "GNU GENERAL PUBLIC LICENSE") && Contains(file.Content, "Version 3")) return "GPL-3.0-only";
            if (Contains(file.Content, "Mozilla Public License") && Contains(file.Content, "2.0")) return "MPL-2.0";
            if (Contains(file.Content, "Redistribution and use in source and binary forms") && Contains(file.Content, "Neither the name")) return "BSD-3-Clause";
        }

        return null;
    }

    private static bool ContainsJsonLicense(string content, string expected)
    {
        try
        {
            using var document = JsonDocument.Parse(content, new JsonDocumentOptions { MaxDepth = 16 });
            return document.RootElement.TryGetProperty("license", out var license)
                && license.ValueKind == JsonValueKind.String
                && string.Equals(license.GetString(), expected, StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static IReadOnlyList<string> ExtractTargets(string content)
    {
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in RuntimeIdentifierRegex().Matches(content)) targets.Add(match.Value.ToLowerInvariant().Replace('/', '-'));
        foreach (Match match in ProjectConfigurationRegex().Matches(content)) targets.Add(match.Value.Replace('\\', '|'));

        var document = TryParseXml(content);
        if (document is not null)
        {
            var targetElements = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "RuntimeIdentifier", "RuntimeIdentifiers", "Platform", "Platforms", "PlatformTarget",
            };
            foreach (var value in document.Descendants()
                         .Where(element => targetElements.Contains(element.Name.LocalName))
                         .SelectMany(element => element.Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
            {
                if (ArchitectureTokenRegex().IsMatch(value)) targets.Add(value);
            }

            foreach (var include in document.Descendants()
                         .Where(element => element.Name.LocalName.Equals("ProjectConfiguration", StringComparison.OrdinalIgnoreCase))
                         .Select(element => element.Attribute("Include")?.Value)
                         .OfType<string>())
            {
                if (ArchitectureTokenRegex().IsMatch(include)) targets.Add(include);
            }
        }

        return Sorted(targets);
    }

    private static XDocument? TryParseXml(string content)
    {
        try
        {
            using var stringReader = new StringReader(content);
            using var xmlReader = XmlReader.Create(stringReader, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
            });
            return XDocument.Load(xmlReader, LoadOptions.None);
        }
        catch (XmlException)
        {
            return null;
        }
    }

    private static bool IsBuildConfiguration(RepositoryFile file) =>
        file.Extension is ".csproj" or ".fsproj" or ".vbproj" or ".vcxproj" or ".props" or ".targets"
        || file.FileName.Equals("CMakeLists.txt", StringComparison.OrdinalIgnoreCase)
        || file.FileName.Equals("Dockerfile", StringComparison.OrdinalIgnoreCase);

    private static bool IsPackagingConfiguration(RepositoryFile file) =>
        IsInstallerFile(file)
        || file.FileName.Equals("Dockerfile", StringComparison.OrdinalIgnoreCase)
        || file.RelativePath.EndsWith("package.json", StringComparison.OrdinalIgnoreCase);

    private static bool IsInstallerFile(RepositoryFile file) =>
        file.Extension.Equals(".wixproj", StringComparison.OrdinalIgnoreCase)
        || file.Extension.Equals(".wxs", StringComparison.OrdinalIgnoreCase)
        || file.Extension.Equals(".msixproj", StringComparison.OrdinalIgnoreCase)
        || file.Extension.Equals(".iss", StringComparison.OrdinalIgnoreCase)
        || file.RelativePath.EndsWith("package.appxmanifest", StringComparison.OrdinalIgnoreCase)
        || (file.RelativePath.EndsWith("package.json", StringComparison.OrdinalIgnoreCase) && Contains(file.Content, "squirrel.windows"));

    private static bool IsCiConfiguration(string path)
    {
        var lowerPath = path.ToLowerInvariant();
        return lowerPath.StartsWith(".github/workflows/", StringComparison.Ordinal)
            || lowerPath.EndsWith("azure-pipelines.yml", StringComparison.Ordinal)
            || lowerPath.EndsWith("azure-pipelines.yaml", StringComparison.Ordinal)
            || lowerPath.EndsWith("appveyor.yml", StringComparison.Ordinal)
            || lowerPath.EndsWith("appveyor.yaml", StringComparison.Ordinal)
            || lowerPath.EndsWith(".gitlab-ci.yml", StringComparison.Ordinal);
    }

    private static bool IsTestFile(RepositoryFile file)
    {
        var path = file.RelativePath.ToLowerInvariant();
        var name = file.FileName.ToLowerInvariant();
        return path.Contains("/tests/", StringComparison.Ordinal)
            || path.StartsWith("tests/", StringComparison.Ordinal)
            || name.EndsWith(".tests.csproj", StringComparison.Ordinal)
            || name.EndsWith(".test.js", StringComparison.Ordinal)
            || name.EndsWith(".test.ts", StringComparison.Ordinal)
            || name.EndsWith(".spec.js", StringComparison.Ordinal)
            || name.EndsWith(".spec.ts", StringComparison.Ordinal)
            || name.StartsWith("test_", StringComparison.Ordinal) && file.Extension.Equals(".py", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("_test.go", StringComparison.Ordinal);
    }

    private static string SelectUiTechnology(IReadOnlyList<string> frameworks, IReadOnlyList<string> projectTypes)
    {
        foreach (var candidate in new[] { "winui3", "wpf", "winforms", "maui", "qt", "electron", "tauri", "flutter" })
        {
            if (frameworks.Contains(candidate, StringComparer.Ordinal)) return candidate;
        }

        if (frameworks.Any(framework => framework is "web" or "react" or "nextjs" or "vue" or "angular" or "aspnet-core")
            || projectTypes.Contains("web", StringComparer.Ordinal))
        {
            return "web";
        }

        return projectTypes.Contains("cli", StringComparer.Ordinal) ? "cli" : "unknown";
    }

    private static void AddFramework(
        string framework,
        string path,
        ISet<string> frameworks,
        IDictionary<string, string> sources)
    {
        frameworks.Add(framework);
        sources.TryAdd(framework, path);
    }

    private static bool IsTrue(IReadOnlyDictionary<string, string> properties, string name) =>
        properties.TryGetValue(name, out var value) && value.Equals("true", StringComparison.OrdinalIgnoreCase);

    private static bool Contains(string? content, string value) =>
        content?.Contains(value, StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsArm64(string target) =>
        target.Contains("arm64", StringComparison.OrdinalIgnoreCase);

    private static bool IsArm64Ec(string target) =>
        target.Contains("arm64ec", StringComparison.OrdinalIgnoreCase);

    private static Evidence FileEvidence(RepositoryFile file, string observation) =>
        new(IsConfigurationFile(file) ? "config" : "file", file.RelativePath, null, observation);

    private static bool IsConfigurationFile(RepositoryFile file) =>
        IsBuildConfiguration(file)
        || IsCiConfiguration(file.RelativePath)
        || IsPackagingConfiguration(file)
        || file.Extension is ".json" or ".xml" or ".yaml" or ".yml" or ".toml";

    private static bool IsSourceCodeFile(RepositoryFile file) =>
        SourceCodeExtensions.Contains(file.Extension);

    private static IReadOnlyList<Evidence> DistinctEvidence(IEnumerable<Evidence> evidence) =>
        evidence
            .DistinctBy(item => $"{item.Path}|{item.Artifact}|{item.Observation}", StringComparer.Ordinal)
            .Take(40)
            .ToArray();

    private static string EvidenceId(string prefix, IEnumerable<Evidence> evidence)
    {
        var canonical = string.Join(
            "\n",
            evidence.Select(item => $"{item.SourceType}|{item.Path}|{item.Artifact}|{item.Observation}"));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        return $"{prefix}-{hash[..20]}";
    }

    private static IReadOnlyList<string> Sorted(IEnumerable<string> values) =>
        values.Order(StringComparer.Ordinal).ToArray();

    [GeneratedRegex(@"(?i)\b(?:win|linux|osx)[-_/]arm64(?:ec)?\b", RegexOptions.CultureInvariant)]
    private static partial Regex RuntimeIdentifierRegex();

    [GeneratedRegex(@"(?i)\b(?:Debug|Release)[|\\](?:ARM64|ARM64EC|x64|x86)\b", RegexOptions.CultureInvariant)]
    private static partial Regex ProjectConfigurationRegex();

    [GeneratedRegex(@"(?i)\b(?:x86|x64|arm|arm64|arm64ec|anycpu|any-cpu)\b", RegexOptions.CultureInvariant)]
    private static partial Regex ArchitectureTokenRegex();

    private sealed record TechnologyScan(
        TechnologyInventory Inventory,
        IReadOnlyDictionary<string, string> FrameworkSources);

    private sealed record WindowsScan(
        WindowsExperience Experience,
        bool OfflineCapabilityEstablished);
}

internal sealed record DiscoveryScan(
    TechnologyInventory Technology,
    BuildFindings BuildFindings,
    WindowsExperience WindowsExperience,
    bool OfflineCapabilityEstablished,
    string? License);