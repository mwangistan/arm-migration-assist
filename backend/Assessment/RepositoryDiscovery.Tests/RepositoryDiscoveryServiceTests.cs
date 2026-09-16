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
        Assert.Contains(assessment.Dependencies,
            dependency => dependency.Name == "electron" && dependency.Ecosystem == "npm");
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
    public async Task DiscoverAsync_ProducesDependencyAndCodeCompatibilityFindings()
    {
        using var repository = TestRepository.Create();
        repository.WriteTrackedFile("SampleApp.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <ItemGroup>
                <PackageReference Include="Contoso.Managed" Version="2.1.0" />
                <PackageReference Include="Contoso.Native.win-x64" Version="4.0.0" />
              </ItemGroup>
            </Project>
            """);
        repository.WriteTrackedFile("package.json",
            """
            {
              "dependencies": { "react": "^19.0.0" },
              "devDependencies": { "vite": "^7.0.0" }
            }
            """);
        repository.WriteTrackedFile("requirements.txt", "requests==2.32.3\n");
        repository.WriteTrackedFile("NativeMethods.cs",
            """
            using System.Runtime.InteropServices;

            internal static class NativeMethods
            {
                [DllImport("legacy-x64.dll")]
                internal static extern int Initialize();
            }
            """);
        repository.WriteTrackedFile("native.cpp",
            """
            #include <immintrin.h>
            int read_value() { return __asm { mov eax, 1 } }
            """);
        repository.CommitChanges("add dependency and compatibility fixtures");

        var assessment = await new RepositoryDiscoveryService().DiscoverAsync(repository.Path);

        Assert.Contains(assessment.Dependencies,
            dependency => dependency.Name == "Contoso.Managed" && dependency.Ecosystem == "nuget");
        Assert.Contains(assessment.Dependencies,
            dependency => dependency.Name == "Contoso.Native.win-x64"
                && dependency.ArchitectureStatus == "emulation-only"
                && dependency.AvailableArchitectures.Contains("x64"));
        Assert.Contains(assessment.Dependencies,
            dependency => dependency.Name == "react" && dependency.Criticality == "required");
        Assert.Contains(assessment.Dependencies,
            dependency => dependency.Name == "vite" && dependency.Criticality == "optional");
        Assert.Contains(assessment.Dependencies,
            dependency => dependency.Name == "requests" && dependency.Ecosystem == "pypi");
        Assert.Contains(assessment.CodeFindings,
            finding => finding.RuleId == "ARM-CODE-PINVOKE-01" && finding.File == "NativeMethods.cs");
        Assert.Contains(assessment.CodeFindings,
            finding => finding.RuleId == "ARM-CODE-INLINE-ASM-01" && finding.File == "native.cpp");
        Assert.Contains(assessment.CodeFindings,
            finding => finding.RuleId == "ARM-CODE-SIMD-01" && finding.File == "native.cpp");
        Assert.Contains("dependency-scanner", assessment.ScanCoverage.ScannersCompleted);
        Assert.Contains("code-compatibility-scanner", assessment.ScanCoverage.ScannersCompleted);
        Assert.DoesNotContain(assessment.Unknowns, unknown => unknown.RequiredSkill == "assessment/dependency-scanner");
        Assert.DoesNotContain(assessment.Unknowns, unknown => unknown.RequiredSkill == "assessment/code-compatibility-scanner");
    }

    [Fact]
    public async Task DiscoverAsync_ParsesTrackedPackagesConfig()
    {
        using var repository = TestRepository.Create();
        repository.WriteTrackedFile("packages.config",
            """
            <?xml version="1.0" encoding="utf-8"?>
            <packages>
              <package id="Contoso.Legacy" version="3.2.1" targetFramework="net48" />
            </packages>
            """);
        repository.CommitChanges("add packages config");

        var assessment = await new RepositoryDiscoveryService().DiscoverAsync(repository.Path);

        Assert.Contains(
            assessment.Dependencies,
            dependency => dependency.Name == "Contoso.Legacy"
                && dependency.Version == "3.2.1"
                && dependency.Ecosystem == "nuget");
        Assert.Equal(assessment.ScanCoverage.FilesTotal, assessment.ScanCoverage.FilesScanned);
    }

    [Fact]
    public async Task DiscoverAsync_ProducesStableAssessmentIdForRepositoryCommitAndRuleset()
    {
        using var repository = TestRepository.Create();
        var service = new RepositoryDiscoveryService();

        var first = await service.DiscoverAsync(repository.Path);
        var second = await service.DiscoverAsync(repository.Path);

        Assert.StartsWith("assessment-", first.AssessmentId, StringComparison.Ordinal);
        Assert.Equal(first.AssessmentId, second.AssessmentId);

        repository.WriteTrackedFile("Program.cs", "Console.WriteLine(\"next commit\");");
        repository.CommitChanges("change assessed commit");
        var changed = await service.DiscoverAsync(repository.Path);

        Assert.NotEqual(first.AssessmentId, changed.AssessmentId);
    }

    [Fact]
    public async Task DiscoverAsync_ReportsEveryCodeRuleOccurrenceWithUniqueEvidenceIds()
    {
        using var repository = TestRepository.Create();
        repository.WriteTrackedFile("NativeMethods.cs",
            """
            using System.Runtime.InteropServices;

            internal static class NativeMethods
            {
                [DllImport("first.dll")]
                internal static extern int First();

                [DllImport("second.dll")]
                internal static extern int Second();
            }
            """);
        repository.CommitChanges("add repeated compatibility findings");

        var assessment = await new RepositoryDiscoveryService().DiscoverAsync(repository.Path);
        var findings = assessment.CodeFindings
            .Where(finding => finding.RuleId == "ARM-CODE-PINVOKE-01")
            .ToArray();

        Assert.Equal(2, findings.Length);
        Assert.Equal(2, findings.Select(finding => finding.EvidenceId).Distinct().Count());
        Assert.Equal([5, 8], findings.Select(finding => finding.Line));
    }

    [Fact]
    public async Task DiscoverAsync_CapsCodeFindingsAtContractLimit()
    {
        using var repository = TestRepository.Create();
        var matchingLine =
            "[DllImport(\"x\")] __asm _mm256_add _M_X64 .ToInt32() NativeLibrary.Load(\"x\");";
        repository.WriteTrackedFile(
            "dense.cpp",
            string.Join('\n', Enumerable.Repeat(matchingLine, 3_334)));
        repository.CommitChanges("add dense compatibility fixture");

        var assessment = await new RepositoryDiscoveryService().DiscoverAsync(repository.Path);

        Assert.Equal(20_000, assessment.CodeFindings.Count);
        Assert.Equal(
            assessment.CodeFindings.Count,
            assessment.CodeFindings.Select(finding => finding.EvidenceId).Distinct().Count());
    }

    [Fact]
    public async Task DiscoverAsync_ParsesMultilineManifestsAndNativeBinaryArchitecture()
    {
        using var repository = TestRepository.Create();
        repository.WriteTrackedFile("pyproject.toml",
            """
            [project]
            dependencies = [
              "fastapi>=0.115",
              "onnxruntime==1.19.0"
            ]

            [project.optional-dependencies]
            test = ["pytest==8.3.0"]
            """);
        repository.WriteTrackedFile("vcpkg.json",
            """{"dependencies":["qtbase",{"name":"openssl","version>=":"3.3.0"}]}""");
        repository.WriteTrackedBytes("runtimes/win-x64/native/legacy.dll", CreatePeImage(0x8664));
        repository.CommitChanges("add multiline and native dependency fixtures");

        var assessment = await new RepositoryDiscoveryService().DiscoverAsync(repository.Path);

        Assert.Contains(assessment.Dependencies,
            dependency => dependency.Name == "fastapi" && dependency.Criticality == "required");
        Assert.Contains(assessment.Dependencies,
            dependency => dependency.Name == "pytest" && dependency.Criticality == "optional");
        Assert.Contains(assessment.Dependencies,
            dependency => dependency.Name == "qtbase" && dependency.Type == "native");
        Assert.Contains(assessment.Dependencies,
            dependency => dependency.Name == "legacy.dll"
                && dependency.Ecosystem == "native-binary"
                && dependency.ArchitectureStatus == "emulation-only"
                && dependency.AvailableArchitectures.Contains("x64"));
    }

    [Fact]
    public async Task DiscoverAsync_DoesNotInferNativeTypeFromGenericPackageNames()
    {
        using var repository = TestRepository.Create();
        repository.WriteTrackedFile("SampleApp.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Contoso.Runtime" Version="1.0.0" />
                <PackageReference Include="Contoso.Interop" Version="2.0.0" />
                                <PackageReference Include="Contoso.Native.Utilities" Version="3.0.0" />
              </ItemGroup>
            </Project>
            """);
        repository.CommitChanges("add generic managed package names");

        var assessment = await new RepositoryDiscoveryService().DiscoverAsync(repository.Path);

        Assert.Equal(
            ["managed", "managed", "managed"],
            assessment.Dependencies
                .Where(dependency => dependency.Name is "Contoso.Runtime"
                    or "Contoso.Interop"
                    or "Contoso.Native.Utilities")
                .OrderBy(dependency => dependency.Name, StringComparer.Ordinal)
                .Select(dependency => dependency.Type));
    }

    [Fact]
    public async Task DiscoverAsync_ReportsMalformedDependencyManifestAsUnknown()
    {
        using var repository = TestRepository.Create();
        repository.WriteTrackedFile("package.json", "{ \"dependencies\": {");
        repository.CommitChanges("add malformed dependency manifest");

        var assessment = await new RepositoryDiscoveryService().DiscoverAsync(repository.Path);

        Assert.Contains(
            assessment.Unknowns,
            unknown => unknown.Area == "dependency"
                && unknown.Description.Contains("could not be parsed", StringComparison.Ordinal)
                && unknown.Description.Contains("package.json", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("package.json", "[]")]
    [InlineData("vcpkg.json", "\"not an object\"")]
    public async Task DiscoverAsync_ReportsNonObjectJsonManifestAsUnknown(
        string manifestPath,
        string content)
    {
        using var repository = TestRepository.Create();
        repository.WriteTrackedFile(manifestPath, content);
        repository.CommitChanges("add non-object dependency manifest");

        var assessment = await new RepositoryDiscoveryService().DiscoverAsync(repository.Path);

        Assert.Contains(
            assessment.Unknowns,
            unknown => unknown.Area == "dependency"
                && unknown.Description.Contains("could not be parsed", StringComparison.Ordinal)
                && unknown.Description.Contains(manifestPath, StringComparison.Ordinal));
    }

    [Fact]
    public async Task DiscoverAsync_RejectsPeHeaderOffsetInsideDosHeader()
    {
        using var repository = TestRepository.Create();
        repository.WriteTrackedBytes("native/forged.dll", CreatePeImage(0x8664, peOffset: 4));
        repository.CommitChanges("add malformed PE fixture");

        var assessment = await new RepositoryDiscoveryService().DiscoverAsync(repository.Path);

        Assert.DoesNotContain(
            assessment.Dependencies,
            dependency => dependency.Ecosystem == "native-binary"
                && dependency.Name == "forged.dll");
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

    [Fact]
    public async Task DiscoverAsync_BoundsRepositoryControlledTextToContractLimits()
    {
        using var repository = TestRepository.Create();
        repository.WriteTrackedFile("SampleApp.csproj",
            $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <Platforms>ARM64-{new string('x', 2_100)}</Platforms>
              </PropertyGroup>
            </Project>
            """);
        repository.CommitChanges("add oversized target text");

        var assessment = await new RepositoryDiscoveryService().DiscoverAsync(repository.Path);

        Assert.All(assessment.BuildFindings.DetectedTargets, target => Assert.InRange(target.Length, 1, 200));
        Assert.All(assessment.BuildFindings.Evidence, evidence => Assert.InRange(evidence.Observation.Length, 1, 2_000));

        var schema = JsonSchema.FromText(await File.ReadAllTextAsync(
            System.IO.Path.Combine(AppContext.BaseDirectory, "RepositoryAssessmentV1.schema.json")));
        var instance = JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(assessment));
        Assert.NotNull(instance);
        var result = schema.Evaluate(instance, new EvaluationOptions { OutputFormat = OutputFormat.List });
        Assert.True(result.IsValid, result.ToString());
    }

    [Fact]
    public async Task DiscoverAsync_RejectsRepositoryNameBeyondContractLimit()
    {
        using var repository = TestRepository.Create(
            $"https://github.com/example/{new string('r', 201)}.git");

        var exception = await Assert.ThrowsAsync<RepositoryDiscoveryException>(
            () => new RepositoryDiscoveryService().DiscoverAsync(repository.Path));

        Assert.Contains("contract limit", exception.Message, StringComparison.Ordinal);
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

    [Fact]
    public async Task DiscoverAsync_ExtractsCleanVisualCppProjectConfigurationTargets()
    {
        using var repository = TestRepository.Create();
        repository.WriteTrackedFile("native.vcxproj",
            """
            <Project>
              <ItemGroup Label="ProjectConfigurations">
                <ProjectConfiguration Include="Debug|x64">
                  <Configuration>Debug</Configuration>
                  <Platform>x64</Platform>
                </ProjectConfiguration>
              </ItemGroup>
            </Project>
            """);
        repository.CommitChanges("add Visual C++ fixture");

        var assessment = await new RepositoryDiscoveryService().DiscoverAsync(repository.Path);

        Assert.Contains("Debug|x64", assessment.BuildFindings.DetectedTargets);
        Assert.DoesNotContain(
            assessment.BuildFindings.DetectedTargets,
            target => target.Any(char.IsWhiteSpace));
    }

    [Fact]
    public async Task DiscoverAsync_ReportsMissingTrackedFileAsUnscannedCoverage()
    {
        using var repository = TestRepository.Create();
        repository.HideAndDeleteTrackedFile("Program.cs");

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
            var fullPath = System.IO.Path.Combine(Path, relativePath);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, content);
        }

        public void WriteTrackedBytes(string relativePath, byte[] content)
        {
            var fullPath = System.IO.Path.Combine(Path, relativePath);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath)!);
            File.WriteAllBytes(fullPath, content);
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

        public void HideAndDeleteTrackedFile(string relativePath)
        {
            RunGit(Path, "update-index", "--assume-unchanged", "--", relativePath);
            File.Delete(System.IO.Path.Combine(Path, relativePath));
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

    private static byte[] CreatePeImage(ushort machine, int peOffset = 128)
    {
        var image = new byte[256];
        image[0] = (byte)'M';
        image[1] = (byte)'Z';
        BitConverter.GetBytes(peOffset).CopyTo(image, 0x3c);
        image[peOffset] = (byte)'P';
        image[peOffset + 1] = (byte)'E';
        BitConverter.GetBytes(machine).CopyTo(image, peOffset + 4);
        return image;
    }
}