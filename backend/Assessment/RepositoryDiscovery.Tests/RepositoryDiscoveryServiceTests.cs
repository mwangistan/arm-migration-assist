using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using ArmMigrationAssist.RepositoryDiscovery;
using ArmMigrationAssist.RepositoryDiscovery.GitHub;
using ArmMigrationAssist.RepositoryDiscovery.Models;
using ArmMigrationAssist.RepositoryDiscovery.Validation;
using Json.Schema;
using Xunit;

namespace ArmMigrationAssist.RepositoryDiscovery.Tests;

public sealed class RepositoryDiscoveryServiceTests
{
    [Fact]
    public async Task ArchiveExtractor_ExtractsOnlyBoundedRegularRepositoryFiles()
    {
        var root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "arm-migration-assist-tests",
            Guid.NewGuid().ToString("N"));
        var archivePath = System.IO.Path.Combine(root, "repository.zip");
        var destination = System.IO.Path.Combine(root, "repository");
        Directory.CreateDirectory(root);

        try
        {
            using (var stream = File.Create(archivePath))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                await WriteArchiveEntryAsync(archive, "owner-repo-sha/src/App.cs", "class App { }");
                await WriteArchiveEntryAsync(archive, "owner-repo-sha/../outside.txt", "outside");
                var symbolicLink = archive.CreateEntry("owner-repo-sha/external-link");
                symbolicLink.ExternalAttributes = 0xA000 << 16;
                await using var linkContent = new StreamWriter(symbolicLink.Open());
                await linkContent.WriteAsync("../outside");
            }

            var result = await RepositoryArchiveExtractor.ExtractAsync(
                archivePath,
                destination,
                CancellationToken.None);

            Assert.Equal(3, result.TotalFiles);
            Assert.Equal(2, result.SkippedFiles);
            Assert.Equal(["src/App.cs"], result.RelativePaths);
            Assert.True(File.Exists(System.IO.Path.Combine(destination, "src", "App.cs")));
            Assert.False(File.Exists(System.IO.Path.Combine(root, "outside.txt")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task DiscoverAsync_AssessesGitHubArchiveWithoutGitCheckout()
    {
        var source = new StubGitHubRepositorySource();

        var assessment = await new RepositoryDiscoveryService(source).DiscoverAsync(
            "https://github.com/example/archive-app");

        Assert.True(source.Called);
        Assert.False(source.UseStoredCredentials);
        Assert.Equal("archive-app", assessment.Repository.Name);
        Assert.Equal(new string('b', 40), assessment.Repository.CommitSha);
        Assert.Contains("csharp", assessment.Technology.Languages);
        Assert.Equal(2, assessment.ScanCoverage.FilesTotal);
        Assert.Equal(2, assessment.ScanCoverage.FilesScanned);
        Assert.NotNull(source.TemporaryRoot);
        Assert.False(Directory.Exists(source.TemporaryRoot));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GitHubRepositorySource_DownloadsCommitPinnedReadOnlyArchive(
        bool useStoredCredentials)
    {
        var root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "arm-migration-assist-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var credentialProvider = new StubGitHubCredentialProvider();
        var handler = new StubGitHubHttpHandler(
            CreateRepositoryArchive(),
            credentialProvider.Token,
            useStoredCredentials);
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.github.com/"),
        };

        try
        {
            var snapshot = await new GitHubRepositorySource(httpClient, credentialProvider)
                .DownloadAsync(
                    "https://github.com/example/archive-app",
                    root,
                    useStoredCredentials,
                    CancellationToken.None);

            Assert.Equal("archive-app", snapshot.Name);
            Assert.Equal(new string('c', 40), snapshot.CommitSha);
            Assert.Equal("main", snapshot.DefaultBranch);
            Assert.Equal(["src/App.cs"], snapshot.Archive.RelativePaths);
            Assert.Equal(useStoredCredentials ? 1 : 0, credentialProvider.CallCount);
            Assert.Equal(3, handler.RequestPaths.Count);
            Assert.EndsWith(
                $"/zipball/{new string('c', 40)}",
                handler.RequestPaths[2],
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task AssessmentValidator_ReportsCrossRecordContractViolations()
    {
        using var repository = TestRepository.Create();
        var assessment = await new RepositoryDiscoveryService().DiscoverAsync(repository.Path);
        var dependency = Assert.Single(assessment.Dependencies);
        var invalid = assessment with
        {
            Dependencies = [dependency, dependency],
            ScanCoverage = assessment.ScanCoverage with
            {
                FilesScanned = assessment.ScanCoverage.FilesTotal + 1,
            },
            Unknowns =
            [
                new AssessmentUnknown(
                    "References missing evidence.",
                    "dependency",
                    null,
                    ["dependency-missing"]),
            ],
        };

        var errors = RepositoryAssessmentValidator.Validate(invalid);

        Assert.Contains(errors, error => error.StartsWith("Duplicate evidenceId", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.StartsWith("filesScanned", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("dependency-missing", StringComparison.Ordinal));
        Assert.Throws<InvalidOperationException>(() => RepositoryAssessmentValidator.EnsureValid(invalid));
    }

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
            dependency => dependency.Name == "electron"
                && dependency.Ecosystem == "npm"
                && dependency.Type == "native");
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
        Assert.DoesNotContain(assessment.Unknowns, unknown => unknown.RequiredSkill == "assessment/dependency-scan");
        Assert.DoesNotContain(assessment.Unknowns, unknown => unknown.RequiredSkill == "assessment/code-compatibility-scan");
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
    public async Task DiscoverAsync_ParsesUtf8BomDependencyManifest()
    {
        using var repository = TestRepository.Create();
        var manifest = """
            <?xml version="1.0" encoding="utf-8"?>
            <packages>
              <package id="Microsoft.Windows.CppWinRT" version="2.0.230706.1" targetFramework="native" />
            </packages>
            """;
        repository.WriteTrackedBytes(
            "packages.config",
            Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(manifest)).ToArray());
        repository.CommitChanges("add BOM packages config");

        var assessment = await new RepositoryDiscoveryService().DiscoverAsync(repository.Path);

        Assert.Contains(
            assessment.Dependencies,
            dependency => dependency.Name == "Microsoft.Windows.CppWinRT"
                && dependency.Version == "2.0.230706.1");
        Assert.DoesNotContain(
            assessment.Unknowns,
            unknown => unknown.Description.Contains("manifest(s) could not be parsed", StringComparison.Ordinal));
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
    public async Task DiscoverAsync_ReportsBorrowedSourceAndInstallerSignals()
    {
        using var repository = TestRepository.Create();
        repository.WriteTrackedFile("native/startup.asm", "mov eax, 1");
        repository.WriteTrackedFile("native/bridge.m", "int bridge(void) { return 0; }");
        repository.WriteTrackedFile("native/simd.cpp",
            """
            #include <nmmintrin.h>
            #if defined(_M_AMD64)
            __m128 value;
            #endif
            """);
        repository.WriteTrackedFile("installer/setup.nsi", "Name ARM64");
        repository.CommitChanges("add borrowed scanner signals");

        var assessment = await new RepositoryDiscoveryService().DiscoverAsync(repository.Path);

        Assert.Contains("assembly", assessment.Technology.Languages);
        Assert.Contains("objective-c", assessment.Technology.Languages);
        Assert.Contains("nsis", assessment.Technology.Installers);
        Assert.Contains(assessment.CodeFindings,
            finding => finding.RuleId == "ARM-CODE-ASSEMBLY-SOURCE-01"
                && finding.File == "native/startup.asm");
        Assert.Contains(assessment.CodeFindings,
            finding => finding.RuleId == "ARM-CODE-SIMD-01"
                && finding.File == "native/simd.cpp");
        Assert.Contains(assessment.CodeFindings,
            finding => finding.RuleId == "ARM-CODE-ARCH-MACRO-01"
                && finding.File == "native/simd.cpp");
        Assert.Equal(assessment.ScanCoverage.FilesTotal, assessment.ScanCoverage.FilesScanned);
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
        repository.WriteTrackedFile("zz-last.asm", "mov eax, 1");
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
        repository.WriteTrackedBytes("python/native_extension.pyd", CreatePeImage(0xaa64));
        repository.WriteTrackedBytes("drivers/legacy.sys", CreatePeImage(0x8664));
        repository.WriteTrackedBytes("native/legacy-arm.dll", CreatePeImage(0x01c4));
        repository.WriteTrackedFile("package.json", """{"dependencies":{"esbuild":"0.25.0"}}""");
        repository.WriteTrackedFile("requirements.txt", "cryptography==44.0.0\n");
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
        Assert.Contains(assessment.Dependencies,
            dependency => dependency.Name == "native_extension.pyd"
                && dependency.Ecosystem == "native-binary"
                && dependency.ArchitectureStatus == "ready"
                && dependency.AvailableArchitectures.Contains("arm64"));
        Assert.Contains(assessment.Dependencies,
            dependency => dependency.Name == "legacy.sys"
                && dependency.Type == "driver"
                && dependency.ArchitectureStatus == "blocked"
                && dependency.AvailableArchitectures.Contains("x64"));
        Assert.Contains(assessment.Dependencies,
            dependency => dependency.Name == "legacy-arm.dll"
                && dependency.ArchitectureStatus == "blocked"
                && dependency.AvailableArchitectures.Contains("arm"));
        Assert.Contains(assessment.Dependencies,
            dependency => dependency.Name == "esbuild" && dependency.Type == "native");
        Assert.Contains(assessment.Dependencies,
            dependency => dependency.Name == "cryptography" && dependency.Type == "native");
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

    private static async Task WriteArchiveEntryAsync(
        ZipArchive archive,
        string path,
        string content)
    {
        var entry = archive.CreateEntry(path);
        await using var writer = new StreamWriter(entry.Open());
        await writer.WriteAsync(content);
    }

    private sealed class StubGitHubRepositorySource : IGitHubRepositorySource
    {
        public bool Called { get; private set; }
        public bool UseStoredCredentials { get; private set; }
        public string? TemporaryRoot { get; private set; }

        public async Task<GitHubRepositorySnapshot> DownloadAsync(
            string repositoryUrl,
            string temporaryRoot,
            bool useStoredCredentials,
            CancellationToken cancellationToken)
        {
            Called = true;
            UseStoredCredentials = useStoredCredentials;
            TemporaryRoot = temporaryRoot;
            var rootPath = System.IO.Path.Combine(temporaryRoot, "repository");
            Directory.CreateDirectory(rootPath);
            await File.WriteAllTextAsync(
                System.IO.Path.Combine(rootPath, "App.cs"),
                "class App { }",
                cancellationToken);
            await File.WriteAllTextAsync(
                System.IO.Path.Combine(rootPath, "App.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\" />",
                cancellationToken);
            return new GitHubRepositorySnapshot(
                "archive-app",
                new string('b', 40),
                "main",
                new ExtractedRepositoryArchive(
                    rootPath,
                    ["App.cs", "App.csproj"],
                    2,
                    0));
        }
    }

    private sealed class StubGitHubCredentialProvider : IGitHubCredentialProvider
    {
        public string Token { get; } = new('x', 40);
        public int CallCount { get; private set; }

        public Task<string?> GetTokenAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult<string?>(Token);
        }
    }

    private sealed class StubGitHubHttpHandler(
        byte[] archive,
        string expectedToken,
        bool expectAuthentication) : HttpMessageHandler
    {
        public List<string> RequestPaths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath
                ?? throw new InvalidOperationException("Request URI is missing.");
            RequestPaths.Add(path);
            if (expectAuthentication)
            {
                Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
                Assert.Equal(expectedToken, request.Headers.Authorization?.Parameter);
            }
            else
            {
                Assert.Null(request.Headers.Authorization);
            }

            HttpContent content = path switch
            {
                "/repos/example/archive-app" => new StringContent(
                    """{"name":"archive-app","default_branch":"main"}""",
                    Encoding.UTF8,
                    "application/json"),
                "/repos/example/archive-app/commits/main" => new StringContent(
                    $"{{\"sha\":\"{new string('c', 40)}\"}}",
                    Encoding.UTF8,
                    "application/json"),
                _ when path.EndsWith($"/zipball/{new string('c', 40)}", StringComparison.Ordinal) =>
                    new ByteArrayContent(archive),
                _ => throw new InvalidOperationException($"Unexpected GitHub API path: {path}"),
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = content,
            });
        }
    }

    private static byte[] CreateRepositoryArchive()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("example-archive-app-sha/src/App.cs");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("class App { }");
        }

        return stream.ToArray();
    }
}