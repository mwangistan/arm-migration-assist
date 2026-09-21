using ArmMigrationAssist.Assessment.DependencyScanner;
using Xunit;

namespace ArmMigrationAssist.RepositoryDiscovery.Tests;

// These tests exercise the pure, network-free verdict logic that classifies registry evidence.
// They must stay offline: no HttpClient, no DependencyRegistryVerifier instance.
public sealed class DependencyRegistryVerifierTests
{
    [Fact]
    public void PyPiVerdict_PureWheel_IsReadyAnyCpu()
    {
        var verdict = DependencyRegistryVerifier.PyPiVerdict(
            new[] { "requests-2.31.0-py3-none-any.whl", "requests-2.31.0.tar.gz" });

        Assert.Equal("ready", verdict.ArchitectureStatus);
        Assert.Contains("any-cpu", verdict.AvailableArchitectures);
    }

    [Fact]
    public void PyPiVerdict_WindowsArm64Wheel_IsReadyArm64()
    {
        var verdict = DependencyRegistryVerifier.PyPiVerdict(
            new[] { "numpy-1.26.0-cp312-cp312-win_arm64.whl", "numpy-1.26.0-cp312-cp312-win_amd64.whl" });

        Assert.Equal("ready", verdict.ArchitectureStatus);
        Assert.Contains("arm64", verdict.AvailableArchitectures);
        Assert.Contains("x64", verdict.AvailableArchitectures);
    }

    [Fact]
    public void PyPiVerdict_WindowsX64Only_IsEmulationOnly()
    {
        var verdict = DependencyRegistryVerifier.PyPiVerdict(
            new[] { "pkg-1.0-cp312-cp312-win_amd64.whl" });

        Assert.Equal("emulation-only", verdict.ArchitectureStatus);
        Assert.Contains("x64", verdict.AvailableArchitectures);
    }

    [Fact]
    public void PyPiVerdict_SourceOnly_IsUnknown()
    {
        var verdict = DependencyRegistryVerifier.PyPiVerdict(new[] { "pkg-1.0.tar.gz" });

        Assert.Equal("unknown", verdict.ArchitectureStatus);
    }

    [Fact]
    public void ParseSimpleIndexFilenames_Pep691Json_ReturnsFilenames()
    {
        const string payload = """
        {
          "meta": { "api-version": "1.0" },
          "name": "click",
          "files": [
            { "filename": "click-8.1.7-py3-none-any.whl", "url": "https://files.example/click-8.1.7-py3-none-any.whl" },
            { "filename": "click-8.1.7.tar.gz", "url": "https://files.example/click-8.1.7.tar.gz" }
          ]
        }
        """;

        var filenames = DependencyRegistryVerifier.ParseSimpleIndexFilenames(payload);

        Assert.Contains("click-8.1.7-py3-none-any.whl", filenames);
        Assert.Contains("click-8.1.7.tar.gz", filenames);
    }

    [Fact]
    public void ParseSimpleIndexFilenames_Pep503Html_ReturnsFilenames()
    {
        const string payload = """
        <!DOCTYPE html>
        <html><body>
          <a href="https://files.example/numpy-1.26.0-cp312-cp312-win_arm64.whl#sha256=abc">numpy-1.26.0-cp312-cp312-win_arm64.whl</a>
          <a href="../../packages/numpy-1.26.0.tar.gz#sha256=def">numpy-1.26.0.tar.gz</a>
        </body></html>
        """;

        var filenames = DependencyRegistryVerifier.ParseSimpleIndexFilenames(payload);

        Assert.Contains("numpy-1.26.0-cp312-cp312-win_arm64.whl", filenames);
        Assert.Contains("numpy-1.26.0.tar.gz", filenames);
    }

    [Fact]
    public void ParseSimpleIndexFilenames_HtmlIndexFeedsArm64Verdict()
    {
        const string payload =
            "<a href=\"https://files.example/numpy-1.26.0-cp312-cp312-win_arm64.whl#sha256=abc\">numpy</a>";

        var verdict = DependencyRegistryVerifier.PyPiVerdict(
            DependencyRegistryVerifier.ParseSimpleIndexFilenames(payload));

        Assert.Equal("ready", verdict.ArchitectureStatus);
        Assert.Contains("arm64", verdict.AvailableArchitectures);
    }

    [Fact]
    public void NpmVerdict_NoRestrictions_IsReadyAnyCpu()
    {
        var verdict = DependencyRegistryVerifier.NpmVerdict(
            cpu: null,
            optionalDependencyNames: Array.Empty<string>());

        Assert.Equal("ready", verdict.ArchitectureStatus);
        Assert.Contains("any-cpu", verdict.AvailableArchitectures);
    }

    [Fact]
    public void NpmVerdict_CpuX64Only_IsEmulationOnly()
    {
        var verdict = DependencyRegistryVerifier.NpmVerdict(
            cpu: new[] { "x64" },
            optionalDependencyNames: Array.Empty<string>());

        Assert.Equal("emulation-only", verdict.ArchitectureStatus);
    }

    [Fact]
    public void NpmVerdict_CpuArm64Windows_IsReadyArm64()
    {
        var verdict = DependencyRegistryVerifier.NpmVerdict(
            cpu: new[] { "arm64" },
            optionalDependencyNames: Array.Empty<string>(),
            os: new[] { "win32" });

        Assert.Equal("ready", verdict.ArchitectureStatus);
        Assert.Contains("arm64", verdict.AvailableArchitectures);
    }

    [Fact]
    public void NpmVerdict_NativeBuildSignals_IsUnknown()
    {
        var verdict = DependencyRegistryVerifier.NpmVerdict(
            cpu: null,
            optionalDependencyNames: Array.Empty<string>(),
            os: null,
            hasNativeBuildSignals: true);

        Assert.Equal("unknown", verdict.ArchitectureStatus);
    }
}
