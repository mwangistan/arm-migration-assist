using ArmMigrationAssist.Api.Assessment;
using ArmMigrationAssist.Api.Assessment.RepositoryDiscovery;

namespace ArmMigrationAssist.Tests;

/// <summary>
/// Story 1.2 - Technology Stack Discovery. Builds temporary repository trees and asserts the
/// detected languages, build systems, package managers, frameworks, and project types.
/// </summary>
public sealed class TechnologyStackDiscoveryTests : IDisposable
{
    private readonly string _root;
    private readonly TechnologyStackDiscoverySkill _skill = new();

    public TechnologyStackDiscoveryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "arm-ma-tech-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private void Write(string relativePath, string content)
    {
        var full = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private TechnologyProfile Discover()
    {
        var snapshot = new RepositorySnapshot
        {
            RunId = "test",
            RepoUrl = "https://github.com/owner/repo",
            WorkspacePath = _root,
            LocalRepoPath = _root
        };
        return _skill.Discover(snapshot);
    }

    // ---- Build systems -------------------------------------------------------

    [Fact]
    public void Detects_python_setuptools_from_setup_py()
    {
        Write("setup.py", "from setuptools import setup\nsetup(name='x')");
        Assert.Contains("setuptools", Discover().BuildSystems);
    }

    [Theory]
    [InlineData("setuptools.build_meta", "setuptools")]
    [InlineData("hatchling.build", "Hatchling")]
    [InlineData("flit_core.buildapi", "Flit")]
    [InlineData("maturin", "maturin")]
    [InlineData("pdm.backend", "PDM")]
    [InlineData("poetry.core.masonry.api", "Poetry")]
    public void Detects_python_build_backend_from_pyproject(string backend, string expected)
    {
        Write("pyproject.toml", $"[build-system]\nrequires = [\"x\"]\nbuild-backend = \"{backend}\"\n");
        Assert.Contains(expected, Discover().BuildSystems);
    }

    [Fact]
    public void Detects_maven_and_gradle()
    {
        Write("pom.xml", "<project></project>");
        Write("sub/build.gradle", "plugins { id 'java' }");
        var p = Discover();
        Assert.Contains("Maven", p.BuildSystems);
        Assert.Contains("Gradle", p.BuildSystems);
    }

    [Theory]
    [InlineData("build.ninja", "Ninja")]
    [InlineData("meson.build", "Meson")]
    [InlineData("WORKSPACE", "Bazel")]
    public void Detects_native_build_systems(string file, string expected)
    {
        Write(file, "# build file");
        Assert.Contains(expected, Discover().BuildSystems);
    }

    [Fact]
    public void Detects_js_bundlers_from_config_and_devdependencies()
    {
        Write("vite.config.ts", "export default {}");
        Write("package.json", """
        { "devDependencies": { "webpack": "^5.0.0" } }
        """);
        var p = Discover();
        Assert.Contains("Vite", p.BuildSystems);
        Assert.Contains("Webpack", p.BuildSystems);
    }

    // ---- Framework detection (structured) -----------------------------------

    [Fact]
    public void PackageJson_react_in_dependencies_is_detected()
    {
        Write("package.json", """
        { "dependencies": { "react": "^18.0.0" } }
        """);
        Assert.Contains("react", Discover().Frameworks);
    }

    [Fact]
    public void PackageJson_similarly_named_package_is_not_a_false_positive()
    {
        // "react-native-foo" must NOT be reported as "react" (the old substring bug).
        Write("package.json", """
        { "dependencies": { "react-native-foo": "^1.0.0" } }
        """);
        Assert.DoesNotContain("react", Discover().Frameworks);
    }

    [Fact]
    public void Csproj_usewpf_is_case_and_whitespace_tolerant()
    {
        Write("App.csproj", """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <UseWPF> true </UseWPF>
          </PropertyGroup>
        </Project>
        """);
        var p = Discover();
        Assert.Contains("wpf", p.Frameworks);
        Assert.Contains("desktop", p.ProjectTypes);
    }

    [Fact]
    public void Csproj_web_sdk_is_detected_as_service()
    {
        Write("Api.csproj", """<Project Sdk="Microsoft.NET.Sdk.Web"></Project>""");
        var p = Discover();
        Assert.Contains("aspnetcore", p.Frameworks);
        Assert.Contains("service", p.ProjectTypes);
    }

    [Fact]
    public void Csproj_winui_packagereference_is_detected()
    {
        Write("App.csproj", """
        <Project Sdk="Microsoft.NET.Sdk">
          <ItemGroup>
            <PackageReference Include="Microsoft.WindowsAppSDK" Version="1.5.0" />
          </ItemGroup>
        </Project>
        """);
        Assert.Contains("winui3", Discover().Frameworks);
    }

    [Fact]
    public void Python_frameworks_from_requirements()
    {
        Write("requirements.txt", "torch==2.1.0\nflask>=3.0\n# a comment\nPyQt6\n");
        var p = Discover();
        Assert.Contains("pytorch", p.Frameworks);
        Assert.Contains("qt", p.Frameworks);
        Assert.Contains("service", p.ProjectTypes);
    }

    // ---- Language stats ------------------------------------------------------

    [Fact]
    public void Vendored_and_minified_files_are_excluded_from_language_stats()
    {
        Write("src/app.ts", new string('a', 100));
        Write("vendor/big.js", new string('b', 100000));
        Write("dist/bundle.min.js", new string('c', 100000));
        var p = Discover();

        Assert.Contains(p.Languages, l => l.Language == "TypeScript");
        // The huge vendored/minified JS must not appear and skew the primary language.
        Assert.DoesNotContain(p.Languages, l => l.Language == "JavaScript");
    }

    [Fact]
    public void New_language_extensions_are_recognized()
    {
        Write("build.gradle.kts", "plugins {}"); // also a Gradle signal
        Write("a.scala", "object A");
        Write("b.dart", "void main() {}");
        Write("c.ps1", "Write-Host hi");
        var langs = Discover().Languages.Select(l => l.Language).ToList();
        Assert.Contains("Scala", langs);
        Assert.Contains("Dart", langs);
        Assert.Contains("PowerShell", langs);
    }

    [Fact]
    public void IsVendoredOrGenerated_flags_expected_paths()
    {
        Assert.True(TechnologyStackDiscoverySkill.IsVendoredOrGenerated("third_party/x/y.c"));
        Assert.True(TechnologyStackDiscoverySkill.IsVendoredOrGenerated("app.min.js"));
        Assert.True(TechnologyStackDiscoverySkill.IsVendoredOrGenerated("gen/service_pb2.py"));
        Assert.False(TechnologyStackDiscoverySkill.IsVendoredOrGenerated("src/main.py"));
    }
}
