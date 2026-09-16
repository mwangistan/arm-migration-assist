using System.Diagnostics;
using System.Text.Json.Nodes;
using ArmMigrationAssist.RepositoryDiscovery;
using Json.Schema;
using Xunit;

namespace ArmMigrationAssist.RepositoryDiscovery.Tests;

public sealed class RepositoryDiscoveryServiceTests
{
    [Fact]
    public async Task DiscoverAsync_ProducesEvidenceBasedAssessmentForTrackedFiles()
    {
        using var repository = TestRepository.Create();
        repository.WriteUntrackedFile(".env", "UNTRACKED_SECRET_MARKER=do-not-emit");

        var assessment = await new RepositoryDiscoveryService().DiscoverAsync(repository.Path);

        Assert.Equal("1.0", assessment.SchemaVersion);
        Assert.Equal("sample-app", assessment.Repository.Name);
        Assert.Equal("https://github.com/example/sample-app", assessment.Repository.Url);
        Assert.Matches("^[0-9a-f]{40}$", assessment.Repository.CommitSha);
        Assert.Equal("main", assessment.Repository.DefaultBranch);
        Assert.Equal("MIT", assessment.Repository.License);
        Assert.Contains("csharp", assessment.Technology.Languages);
        Assert.Contains("javascript", assessment.Technology.Languages);
        Assert.Contains("aspnet-core", assessment.Technology.Frameworks);
        Assert.Contains("electron", assessment.Technology.Frameworks);
        Assert.Contains("msbuild", assessment.Technology.BuildSystems);
        Assert.Contains("nuget", assessment.Technology.PackageManagers);
        Assert.Contains("npm", assessment.Technology.PackageManagers);
        Assert.Contains("github-actions", assessment.Technology.CiSystems);
        Assert.True(assessment.BuildFindings.Arm64TargetExists);
        Assert.True(assessment.BuildFindings.Arm64CiJobExists);
        Assert.True(assessment.BuildFindings.TestsExist);
        Assert.Equal("electron", assessment.WindowsExperience.UiTechnology);
        Assert.Empty(assessment.Dependencies);
        Assert.Empty(assessment.CodeFindings);
        Assert.Equal(assessment.ScanCoverage.FilesTotal, assessment.ScanCoverage.FilesScanned);
        Assert.DoesNotContain(
            "UNTRACKED_SECRET_MARKER",
            System.Text.Json.JsonSerializer.Serialize(assessment),
            StringComparison.Ordinal);
        Assert.All(
            assessment.BuildFindings.Evidence.Concat(assessment.WindowsExperience.Evidence),
            evidence =>
            {
                if (evidence.Path is not null)
                {
                    Assert.False(System.IO.Path.IsPathRooted(evidence.Path));
                    Assert.DoesNotContain("..", evidence.Path);
                    Assert.DoesNotContain(repository.Path, evidence.Observation, StringComparison.OrdinalIgnoreCase);
                }
            });
    }

    [Fact]
    public async Task Command_WritesAssessmentThatMatchesRepositoryAssessmentV1Schema()
    {
        using var repository = TestRepository.Create();
        var outputDirectory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "arm-migration-assist-tests",
            Guid.NewGuid().ToString("N"));
        var outputPath = System.IO.Path.Combine(outputDirectory, "assessment.json");

        try
        {
            var exitCode = await RepositoryDiscoveryCommand.RunAsync(
                [repository.Path, "--output", outputPath],
                TextWriter.Null,
                TextWriter.Null);

            Assert.Equal(0, exitCode);
            var schema = JsonSchema.FromText(await File.ReadAllTextAsync(
                System.IO.Path.Combine(AppContext.BaseDirectory, "RepositoryAssessmentV1.schema.json")));
            var instance = JsonNode.Parse(await File.ReadAllTextAsync(outputPath));
            Assert.NotNull(instance);
            var result = schema.Evaluate(instance, new EvaluationOptions
            {
                OutputFormat = OutputFormat.List,
            });
            Assert.True(result.IsValid, result.ToString());
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData("http://github.com/example/repository")]
    [InlineData("https://user:token@github.com/example/repository")]
    [InlineData("https://git.example.test/example/repository")]
    [InlineData("https://github.com/example/repository/issues")]
    public async Task DiscoverAsync_RejectsNonAnonymousNonGitHubRepositoryUrls(string source)
    {
        var exception = await Assert.ThrowsAsync<RepositoryDiscoveryException>(
            () => new RepositoryDiscoveryService().DiscoverAsync(source));

        Assert.Contains("GitHub", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DiscoverAsync_RejectsTrackedChangesSoCommitMetadataRemainsReproducible()
    {
        using var repository = TestRepository.Create();
        repository.WriteTrackedFile("Program.cs", "Console.WriteLine(\"changed\");");

        var exception = await Assert.ThrowsAsync<RepositoryDiscoveryException>(
            () => new RepositoryDiscoveryService().DiscoverAsync(repository.Path));

        Assert.Contains("tracked changes", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DiscoverAsync_DoesNotTreatArm64DocumentationAsABuildTarget()
    {
        using var repository = TestRepository.Create();
        repository.WriteTrackedFile("SampleApp.csproj",
            """<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>""");
        repository.WriteTrackedFile(".github/workflows/build.yml",
            """jobs: { build: { runs-on: windows-latest } }""");
        repository.WriteTrackedFile("README.md",
            """
            ARM64 and win-arm64 support are planned but are not configured.
            Future work may use aria-label, serviceWorker.register, ToastNotificationManager,
            and EnteredBackground APIs.
            """);
        repository.CommitChanges("remove ARM64 configuration");

        var assessment = await new RepositoryDiscoveryService().DiscoverAsync(repository.Path);

        Assert.False(assessment.BuildFindings.Arm64TargetExists);
        Assert.False(assessment.BuildFindings.Arm64CiJobExists);
        Assert.DoesNotContain(assessment.BuildFindings.Evidence, evidence => evidence.Path == "README.md");
        Assert.False(assessment.WindowsExperience.WindowsVersionExists);
        Assert.False(assessment.WindowsExperience.OfflineCapable);
        Assert.Equal("unknown", assessment.WindowsExperience.AccessibilityEvidence);
        Assert.False(assessment.WindowsExperience.NotificationsIntegrated);
        Assert.False(assessment.WindowsExperience.LifecycleIntegrated);
        Assert.DoesNotContain(assessment.WindowsExperience.Evidence, evidence => evidence.Path == "README.md");
    }

    [Fact]
    public async Task DiscoverAsync_ReportsArm64EcSeparatelyFromNativeArm64()
    {
        using var repository = TestRepository.Create();
        repository.WriteTrackedFile("SampleApp.csproj",
            """<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><Platforms>x64;ARM64EC</Platforms></PropertyGroup></Project>""");
        repository.WriteTrackedFile(".github/workflows/build.yml",
            """jobs: { build: { runs-on: windows-latest } }""");
        repository.CommitChanges("configure Arm64EC");

        var assessment = await new RepositoryDiscoveryService().DiscoverAsync(repository.Path);

        Assert.False(assessment.BuildFindings.Arm64TargetExists);
        Assert.True(assessment.BuildFindings.Arm64EcTargetExists);
    }

    [Fact]
    public async Task DiscoverAsync_NormalizesSshOriginWithoutPublishingSshDetails()
    {
        using var repository = TestRepository.Create("git@github.com:example/sample-app.git");

        var assessment = await new RepositoryDiscoveryService().DiscoverAsync(repository.Path);

        Assert.Equal("https://github.com/example/sample-app", assessment.Repository.Url);
    }

    [Fact]
    public async Task DiscoverAsync_DetectsMixedNativePythonBuildAndCiTechnologies()
    {
        using var repository = TestRepository.Create();
        repository.WriteTrackedFile("native.cpp", "#include <QtCore>\nint main() { return 0; }");
        repository.WriteTrackedFile("CMakeLists.txt", "find_package(Qt6 REQUIRED COMPONENTS Core)");
        repository.WriteTrackedFile("pyproject.toml",
            """
            [build-system]
            requires = ["setuptools"]
            [project]
            dependencies = ["torch==2.4.0"]
            """);
        repository.WriteTrackedFile("worker.py", "import torch\n");
        repository.WriteTrackedFile("vcpkg.json", """{"dependencies":["qtbase"]}""");
        repository.WriteTrackedFile("Dockerfile", "FROM --platform=linux/arm64 python:3.12-slim");
        repository.WriteTrackedFile("azure-pipelines.yml",
            """steps: [{ script: "docker build --platform linux/arm64 ." }]""");
        repository.CommitChanges("add mixed technology fixtures");

        var assessment = await new RepositoryDiscoveryService().DiscoverAsync(repository.Path);

        Assert.Contains("cpp", assessment.Technology.Languages);
        Assert.Contains("python", assessment.Technology.Languages);
        Assert.Contains("qt", assessment.Technology.Frameworks);
        Assert.Contains("pytorch", assessment.Technology.Frameworks);
        Assert.Contains("cmake", assessment.Technology.BuildSystems);
        Assert.Contains("docker", assessment.Technology.BuildSystems);
        Assert.Contains("pep517", assessment.Technology.BuildSystems);
        Assert.Contains("pip", assessment.Technology.PackageManagers);
        Assert.Contains("vcpkg", assessment.Technology.PackageManagers);
        Assert.Contains("azure-pipelines", assessment.Technology.CiSystems);
        Assert.True(assessment.BuildFindings.PackagingSupportsArm64);
        Assert.True(assessment.BuildFindings.Arm64CiJobExists);
    }

    [Fact]
    public async Task DiscoverAsync_ReportsOversizedTrackedTextAsUnscannedCoverage()
    {
        using var repository = TestRepository.Create();
        repository.WriteTrackedFile("oversized.json", new string('x', 1_048_577));
        repository.CommitChanges("add oversized fixture");

        var assessment = await new RepositoryDiscoveryService().DiscoverAsync(repository.Path);

        Assert.Equal(assessment.ScanCoverage.FilesTotal - 1, assessment.ScanCoverage.FilesScanned);
        Assert.Contains(
            assessment.Unknowns,
            unknown => unknown.Area == "scan-coverage" && unknown.Description.StartsWith("1 tracked file", StringComparison.Ordinal));
    }

    private sealed class TestRepository : IDisposable
    {
        private TestRepository(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TestRepository Create(string remoteUrl = "https://github.com/example/sample-app.git")
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "arm-migration-assist-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(System.IO.Path.Combine(path, ".github", "workflows"));
            Directory.CreateDirectory(System.IO.Path.Combine(path, "tests"));

            File.WriteAllText(System.IO.Path.Combine(path, "SampleApp.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk.Web">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <RuntimeIdentifiers>win-x64;win-arm64</RuntimeIdentifiers>
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(System.IO.Path.Combine(path, "Program.cs"), "Console.WriteLine(\"sample\");");
            File.WriteAllText(System.IO.Path.Combine(path, "package.json"),
                """{"dependencies":{"electron":"^32.0.0"}}""");
            File.WriteAllText(System.IO.Path.Combine(path, "main.js"),
                "const { app } = require('electron'); app.whenReady();");
            File.WriteAllText(System.IO.Path.Combine(path, "tests", "SampleApp.Tests.csproj"),
                """<Project Sdk="Microsoft.NET.Sdk" />""");
            File.WriteAllText(System.IO.Path.Combine(path, ".github", "workflows", "build.yml"),
                """
                jobs:
                  build-arm64:
                    runs-on: windows-latest
                    steps:
                      - run: dotnet publish -r win-arm64
                """);
            File.WriteAllText(System.IO.Path.Combine(path, "LICENSE"), "MIT License\n");

            RunGit(path, "init", "--initial-branch=main");
            RunGit(path, "config", "user.name", "Repository Discovery Test");
            RunGit(path, "config", "user.email", "test@example.invalid");
            RunGit(path, "remote", "add", "origin", remoteUrl);
            RunGit(path, "add", ".");
            RunGit(path, "commit", "-m", "fixture");

            return new TestRepository(path);
        }

        public void Dispose()
        {
            foreach (var file in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(Path, recursive: true);
        }

        public void WriteTrackedFile(string relativePath, string content)
        {
            File.WriteAllText(System.IO.Path.Combine(Path, relativePath), content);
        }

        public void WriteUntrackedFile(string relativePath, string content)
        {
            File.WriteAllText(System.IO.Path.Combine(Path, relativePath), content);
        }

        public void CommitChanges(string message)
        {
            RunGit(Path, "add", ".");
            RunGit(Path, "commit", "-m", message);
        }

        private static void RunGit(string workingDirectory, params string[] arguments)
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    WorkingDirectory = workingDirectory,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                },
            };
            process.StartInfo.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
            process.StartInfo.Environment["GIT_CONFIG_GLOBAL"] = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";
            process.StartInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
            foreach (var argument in arguments)
            {
                process.StartInfo.ArgumentList.Add(argument);
            }

            process.Start();
            var standardError = process.StandardError.ReadToEnd();
            process.WaitForExit();
            Assert.True(process.ExitCode == 0, standardError);
        }
    }
}