using ArmMigrationAssist.Api.Assessment;
using ArmMigrationAssist.Api.Assessment.DependencyScanner;
using System.IO.Compression;
using System.Net;

namespace ArmMigrationAssist.Tests;

/// <summary>
/// Tests for the registry-verification decision logic. The verdict functions are pure and
/// static, so they are exercised here without any network access.
/// </summary>
public sealed class RegistryVerifierTests
{
    [Fact]
    public void PyPi_PureWheel_Is_Ready_AnyCpu()
    {
        var v = DependencyRegistryVerifier.PyPiVerdict(new[] { "requests-2.31.0-py3-none-any.whl" });
        Assert.Equal(DependencyClassification.Arm64Ready, v.Classification);
        Assert.Equal(new[] { "any-cpu" }, v.AvailableArchitectures);
    }

    [Fact]
    public void PyPi_WinArm64_Wheel_Is_Ready_Arm64()
    {
        var v = DependencyRegistryVerifier.PyPiVerdict(new[]
        {
            "numpy-2.2.0-cp312-cp312-win_amd64.whl",
            "numpy-2.2.0-cp312-cp312-win_arm64.whl"
        });
        Assert.Equal(DependencyClassification.Arm64Ready, v.Classification);
        Assert.Contains("arm64", v.AvailableArchitectures);
    }

    [Fact]
    public void PyPi_X64_Only_Is_EmulationOnly()
    {
        var v = DependencyRegistryVerifier.PyPiVerdict(new[]
        {
            "numpy-2.1.3-cp312-cp312-win32.whl",
            "numpy-2.1.3-cp312-cp312-win_amd64.whl"
        });
        Assert.Equal(DependencyClassification.EmulationOnly, v.Classification);
        Assert.Equal(new[] { "x64" }, v.AvailableArchitectures);
    }

    [Fact]
    public void PyPi_SourceOnly_Is_Unknown()
    {
        var v = DependencyRegistryVerifier.PyPiVerdict(new[] { "somepkg-1.0.0.tar.gz" });
        Assert.Equal(DependencyClassification.Unknown, v.Classification);
        Assert.Equal(new[] { "unknown" }, v.AvailableArchitectures);
    }

    [Fact]
    public void Npm_No_Cpu_No_Platform_Is_Ready_AnyCpu()
    {
        var v = DependencyRegistryVerifier.NpmVerdict(cpu: null, optionalDepNames: Array.Empty<string>());
        Assert.Equal(DependencyClassification.Arm64Ready, v.Classification);
        Assert.Equal(new[] { "any-cpu" }, v.AvailableArchitectures);
    }

    [Fact]
    public void Npm_Cpu_Restricted_To_X64_Is_EmulationOnly()
    {
        var v = DependencyRegistryVerifier.NpmVerdict(cpu: new[] { "x64" }, optionalDepNames: Array.Empty<string>());
        Assert.Equal(DependencyClassification.EmulationOnly, v.Classification);
    }

    [Fact]
    public void Npm_Arm64_Platform_Dependency_Is_Ready()
    {
        var v = DependencyRegistryVerifier.NpmVerdict(
            cpu: null,
            optionalDepNames: new[] { "@img/sharp-win32-x64", "@img/sharp-win32-arm64" });
        Assert.Equal(DependencyClassification.Arm64Ready, v.Classification);
        Assert.Contains("arm64", v.AvailableArchitectures);
    }

    [Fact]
    public void Npm_Platform_Deps_Without_Arm64_Is_EmulationOnly()
    {
        var v = DependencyRegistryVerifier.NpmVerdict(
            cpu: null,
            optionalDepNames: new[] { "esbuild-win32-x64", "esbuild-linux-x64" });
        Assert.Equal(DependencyClassification.EmulationOnly, v.Classification);
    }

    [Fact]
    public void NuGet_AnyCpu_Managed_Assembly_Is_Ready()
    {
        using var package = CreatePackage(
            ("lib/net10.0/ArmMigrationAssist.Tests.dll", File.ReadAllBytes(typeof(RegistryVerifierTests).Assembly.Location)));

        var v = DependencyRegistryVerifier.AnalyzeNuGetPackage(package);

        Assert.Equal(DependencyClassification.Arm64Ready, v.Classification);
        Assert.Equal(new[] { "any-cpu" }, v.AvailableArchitectures);
    }

    [Fact]
    public void NuGet_AnyCpu_BuildTool_Assembly_Is_Ready()
    {
        using var package = CreatePackage(
            ("tools/net10.0/BuildTask.dll", File.ReadAllBytes(typeof(RegistryVerifierTests).Assembly.Location)));

        var v = DependencyRegistryVerifier.AnalyzeNuGetPackage(package);

        Assert.Equal(DependencyClassification.Arm64Ready, v.Classification);
        Assert.Equal(new[] { "any-cpu" }, v.AvailableArchitectures);
    }

    [Fact]
    public void NuGet_Arm64_And_X64_Runtime_Assets_Are_Ready()
    {
        using var package = CreatePackage(
            ("runtimes/win-arm64/native/example.dll", [1]),
            ("runtimes/win-x64/native/example.dll", [1]));

        var v = DependencyRegistryVerifier.AnalyzeNuGetPackage(package);

        Assert.Equal(DependencyClassification.Arm64Ready, v.Classification);
        Assert.Equal(new[] { "arm64", "x64" }, v.AvailableArchitectures);
    }

    [Fact]
    public void NuGet_X64_Only_Runtime_Asset_Is_EmulationOnly()
    {
        using var package = CreatePackage(("runtimes/win-x64/native/example.dll", [1]));

        var v = DependencyRegistryVerifier.AnalyzeNuGetPackage(package);

        Assert.Equal(DependencyClassification.EmulationOnly, v.Classification);
        Assert.Equal(new[] { "x64" }, v.AvailableArchitectures);
    }

    [Fact]
    public void NuGet_Arm32_Only_Runtime_Asset_Is_Blocked()
    {
        using var package = CreatePackage(("runtimes/win-arm/native/example.dll", [1]));

        var v = DependencyRegistryVerifier.AnalyzeNuGetPackage(package);

        Assert.Equal(DependencyClassification.Blocked, v.Classification);
        Assert.Equal(new[] { "arm" }, v.AvailableArchitectures);
    }

    [Fact]
    public void NuGet_MetaPackage_Without_Runtime_Payload_Is_Unknown()
    {
        using var package = CreatePackage(("example.nuspec", "<package />"u8.ToArray()));

        var v = DependencyRegistryVerifier.AnalyzeNuGetPackage(package);

        Assert.Equal(DependencyClassification.Unknown, v.Classification);
        Assert.Equal(new[] { "unknown" }, v.AvailableArchitectures);
    }

    [Fact]
    public void NuGet_HeaderOnly_Source_Package_Is_Ready()
    {
        using var package = CreatePackage(("include/wil/resource.h", "#pragma once"u8.ToArray()));

        var v = DependencyRegistryVerifier.AnalyzeNuGetPackage(package);

        Assert.Equal(DependencyClassification.Arm64Ready, v.Classification);
        Assert.Equal(new[] { "any-cpu" }, v.AvailableArchitectures);
    }

    [Fact]
    public async Task NuGet_VerifyAsync_Downloads_And_Inspects_Exact_Version()
    {
        using var package = CreatePackage(
            ("lib/net10.0/ArmMigrationAssist.Tests.dll", File.ReadAllBytes(typeof(RegistryVerifierTests).Assembly.Location)));
        using var client = new HttpClient(new PackageHandler(package.ToArray()));
        var verifier = new DependencyRegistryVerifier(client);

        var v = await verifier.VerifyAsync("nuget", "Example.Package", "1.2.3");

        Assert.NotNull(v);
        Assert.Equal(DependencyClassification.Arm64Ready, v.Classification);
        Assert.Equal(new[] { "any-cpu" }, v.AvailableArchitectures);
    }

    [Fact]
    public async Task NuGet_Cfs_Miss_Does_Not_Fall_Back_To_NugetOrg()
    {
        using var client = new HttpClient(new NotFoundHandler());
        var verifier = new DependencyRegistryVerifier(client);

        var v = await verifier.VerifyAsync("nuget", "Missing.Package", "0.0.0.0");

        Assert.Null(v);
    }

    [Fact]
    public void Verified_Finding_Raises_Confidence_And_Uses_Verified_Arches()
    {
        var snapshot = new RepositorySnapshot
        {
            RunId = "run-rv-0001",
            RepoUrl = "https://github.com/example/py-app",
            WorkspacePath = "/ws",
            LocalRepoPath = "/ws/repo",
            CommitSha = "0123456789abcdef0123456789abcdef01234567",
            DefaultBranch = "main",
            FileCount = 10
        };
        var m = new ReadinessManifest { Repository = snapshot, Target = MigrationTarget.Arm64Native };
        m.Skills.Add(new SkillInfo("dependency-scan", "1.0.0", "Scans deps.", false, ["repository-snapshot"], ["dependencies"]));
        m.Dependencies =
        [
            new DependencyFinding
            {
                Id = FindingId.Compute("pypi", "pillow", "req"),
                Name = "pillow", Version = "10.0.0", Source = "pypi",
                Classification = DependencyClassification.EmulationOnly,
                EvidencePath = "requirements.txt",
                AvailableArchitectures = ["x64"],
                RegistryVerified = true
            }
        ];

        var doc = Api.Assessment.Contract.AssessmentV1Mapper.Map(m);
        var dep = doc.Dependencies.Single();

        Assert.Equal("emulation-only", dep.ArchitectureStatus);
        Assert.Equal(new[] { "x64" }, dep.AvailableArchitectures);
        Assert.Equal(0.9, dep.Confidence);   // verified, decided
        Assert.Empty(Api.Assessment.Contract.EvidenceValidator.Validate(doc));
    }

    private static MemoryStream CreatePackage(params (string Path, byte[] Content)[] files)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in files)
            {
                var entry = archive.CreateEntry(file.Path);
                using var output = entry.Open();
                output.Write(file.Content);
            }
        }
        stream.Position = 0;
        return stream;
    }

    private sealed class PackageHandler(byte[] package) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.EndsWith(
                "/example.package/1.2.3/example.package.1.2.3.nupkg",
                request.RequestUri!.AbsolutePath,
                StringComparison.Ordinal);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(package)
            });
        }
    }

    private sealed class NotFoundHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.NotEqual("api.nuget.org", request.RequestUri!.Host);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
