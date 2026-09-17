using ArmMigrationAssist.Api.Assessment;
using ArmMigrationAssist.Api.Assessment.Contract;
using ArmMigrationAssist.Api.Assessment.DependencyScanner;

namespace ArmMigrationAssist.Tests;

/// <summary>
/// Tests for the Microsoft component-detection integration: manifest parsing (independent of
/// the executable) and the ecosystem-aware ARM64 classification / V1 mapping it feeds.
/// </summary>
public sealed class ComponentDetectionTests
{
    // A representative slice of a real component-detection --ManifestFile output.
    private const string SampleManifest = """
    {
      "componentsFound": [
        {
          "locationsFoundAt": ["/requirements.txt"],
          "component": { "type": "Pip", "name": "numpy", "version": "1.26.4",
            "packageUrl": { "Type": "pypi", "Name": "numpy", "Version": "1.26.4" }, "id": "numpy 1.26.4 - pip" },
          "detectorId": "PipReport",
          "isDevelopmentDependency": false,
          "topLevelReferrers": []
        },
        {
          "locationsFoundAt": ["/requirements.txt"],
          "component": { "type": "Pip", "name": "requests", "version": "2.31.0",
            "packageUrl": { "Type": "pypi" }, "id": "requests 2.31.0 - pip" },
          "detectorId": "PipReport",
          "isDevelopmentDependency": false,
          "topLevelReferrers": [ { "id": "app" } ]
        },
        {
          "locationsFoundAt": ["/package.json"],
          "component": { "type": "Npm", "name": "sharp", "version": "0.33.0",
            "packageUrl": { "Type": "npm" }, "id": "sharp 0.33.0 - Npm" },
          "detectorId": "Npm",
          "isDevelopmentDependency": true,
          "topLevelReferrers": []
        },
        {
          "locationsFoundAt": ["/Cargo.toml"],
          "component": { "type": "Cargo", "name": "serde", "version": "1.0.0",
            "packageUrl": { "Type": "cargo" }, "id": "serde 1.0.0 - Cargo" },
          "detectorId": "RustSbom",
          "topLevelReferrers": []
        },
        {
          "locationsFoundAt": ["/go.mod"],
          "component": { "type": "Go", "name": "golang.org/x/sys", "version": "0.1.0",
            "packageUrl": { "Type": "golang" }, "id": "golang.org/x/sys 0.1.0 - Go" },
          "detectorId": "Go",
          "topLevelReferrers": []
        }
      ]
    }
    """;

    [Fact]
    public void ParseManifest_Reads_All_Components_With_Ecosystems()
    {
        var records = ComponentDetectionScanner.ParseManifest(SampleManifest);

        Assert.Equal(5, records.Count);
        Assert.Contains(records, r => r.Name == "numpy" && r.Ecosystem == "pypi" && r.Version == "1.26.4");
        Assert.Contains(records, r => r.Name == "sharp" && r.Ecosystem == "npm");
        Assert.Contains(records, r => r.Name == "serde" && r.Ecosystem == "cargo");
        // golang purl type is normalized to "go".
        Assert.Contains(records, r => r.Ecosystem == "go");
        // locations have their leading slash stripped.
        Assert.All(records, r => Assert.DoesNotContain(r.Locations, l => l.StartsWith('/')));
    }

    [Fact]
    public void ParseManifest_Captures_Dev_And_Direct_Flags()
    {
        var records = ComponentDetectionScanner.ParseManifest(SampleManifest);

        var numpy = records.Single(r => r.Name == "numpy");
        Assert.False(numpy.IsDevelopment);
        Assert.True(numpy.IsDirect);                       // empty topLevelReferrers => direct

        var requests = records.Single(r => r.Name == "requests");
        Assert.False(requests.IsDirect);                   // has a top-level referrer => transitive

        var sharp = records.Single(r => r.Name == "sharp");
        Assert.True(sharp.IsDevelopment);
    }

    [Fact]
    public void ParseManifest_Empty_Or_Missing_Is_Safe()
    {
        Assert.Empty(ComponentDetectionScanner.ParseManifest("{}"));
        Assert.Empty(ComponentDetectionScanner.ParseManifest("""{ "componentsFound": [] }"""));
    }

    [Fact]
    public void NativePython_Package_Is_Unknown_PurePython_Is_Ready()
    {
        var numpy = ComponentDetectionScanner.ParseManifest(SampleManifest).Single(r => r.Name == "numpy");
        var requests = ComponentDetectionScanner.ParseManifest(SampleManifest).Single(r => r.Name == "requests");

        Assert.Equal(DependencyClassification.Unknown, DependencyScanSkill.MapComponent(numpy).Classification);
        Assert.Equal(DependencyClassification.Arm64Ready, DependencyScanSkill.MapComponent(requests).Classification);
    }

    [Fact]
    public void DevDependency_Maps_To_Optional_Criticality()
    {
        var sharp = ComponentDetectionScanner.ParseManifest(SampleManifest).Single(r => r.Name == "sharp");
        var finding = DependencyScanSkill.MapComponent(sharp);

        var manifest = MinimalManifest();
        manifest.Dependencies = [finding];
        var doc = AssessmentV1Mapper.Map(manifest);

        Assert.Equal("optional", doc.Dependencies.Single().Criticality);
        Assert.Empty(EvidenceValidator.Validate(doc));
    }

    [Fact]
    public void Direct_And_Transitive_Relationships_Are_Preserved_Internally()
    {
        var records = ComponentDetectionScanner.ParseManifest(SampleManifest);
        DependencyFinding[] dependencies =
        [
            DependencyScanSkill.MapComponent(records.Single(r => r.Name == "numpy")),
            DependencyScanSkill.MapComponent(records.Single(r => r.Name == "requests"))
        ];

        Assert.True(dependencies.Single(d => d.Name == "numpy").IsDirect);
        Assert.False(dependencies.Single(d => d.Name == "requests").IsDirect);
    }

    [Fact]
    public void NuGet_Explicit_X86_Version_Maps_To_EmulationOnly()
    {
        var component = new ComponentRecord(
            "Microsoft.Telemetry.Inbox.Native",
            "10.0.19041.1-191206-1406.vb-release.x86fre",
            "nuget",
            "NuGet",
            ["packages.config"],
            false,
            true);

        var finding = DependencyScanSkill.MapComponent(component);

        Assert.Equal(DependencyClassification.EmulationOnly, finding.Classification);
        Assert.Equal(new[] { "x86" }, finding.AvailableArchitectures);
    }

    [Fact]
    public void All_Ecosystems_Map_To_Valid_Schema_Values()
    {
        var manifest = MinimalManifest();
        manifest.Dependencies = ComponentDetectionScanner.ParseManifest(SampleManifest)
            .Select(DependencyScanSkill.MapComponent).ToList();

        var doc = AssessmentV1Mapper.Map(manifest);
        var validTypes = new[] { "managed", "native", "com", "plugin", "driver", "unknown" };
        var validStatus = new[] { "ready", "emulation-only", "unknown", "blocked" };

        foreach (var d in doc.Dependencies)
        {
            Assert.Contains(d.Type, validTypes);
            Assert.Contains(d.ArchitectureStatus, validStatus);
            Assert.False(string.IsNullOrWhiteSpace(d.Ecosystem));
            Assert.NotEmpty(d.Evidence);
        }
        // pypi/cargo/go all normalize to known ecosystem tokens.
        Assert.Contains(doc.Dependencies, d => d.Ecosystem == "pypi" && d.Type == "managed");
        Assert.Contains(doc.Dependencies, d => d.Ecosystem == "cargo" && d.Type == "native");
        Assert.Contains(doc.Dependencies, d => d.Ecosystem == "go" && d.Type == "native");
        Assert.Empty(EvidenceValidator.Validate(doc));
    }

    private static ReadinessManifest MinimalManifest()
    {
        var snapshot = new RepositorySnapshot
        {
            RunId = "run-cd-0001",
            RepoUrl = "https://github.com/example/py-app",
            WorkspacePath = "/ws",
            LocalRepoPath = "/ws/repo",
            CommitSha = "0123456789abcdef0123456789abcdef01234567",
            DefaultBranch = "main",
            FileCount = 50
        };
        var m = new ReadinessManifest { Repository = snapshot, Target = MigrationTarget.Arm64Native };
        m.FilesScanned = 50;
        m.ScannersCompleted.Add("component-detection");
        m.Skills.Add(new SkillInfo("dependency-scan", "1.0.0", "Scans deps.", false, ["repository-snapshot"], ["dependencies"]));
        return m;
    }
}
