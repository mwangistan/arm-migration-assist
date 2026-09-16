using System.Text.Json;
using System.Text.RegularExpressions;
using ArmMigrationAssist.Api.Assessment;
using ArmMigrationAssist.Api.Assessment.Contract;

namespace ArmMigrationAssist.Tests;

/// <summary>
/// Conformance tests for the RepositoryAssessmentV1 wire contract. A full JSON-Schema
/// validator is not available offline, so these tests read the actual constraints
/// (required lists, enums, patterns) from RepositoryAssessmentV1.schema.json and assert the
/// mapper's output satisfies them - plus the cross-record EvidenceValidator rules.
/// </summary>
public sealed class SchemaConformanceTests
{
    private static readonly JsonElement Schema = LoadSchema();
    private static readonly RepositoryAssessmentV1 Doc = AssessmentV1Mapper.Map(BuildManifest());
    private static readonly JsonElement Json = JsonDocument.Parse(AssessmentV1Mapper.Serialize(Doc)).RootElement;

    private static JsonElement LoadSchema()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "RepositoryAssessmentV1.schema.json");
        return JsonDocument.Parse(File.ReadAllText(path)).RootElement.Clone();
    }

    [Fact]
    public void Emits_All_Required_TopLevel_Fields()
    {
        foreach (var req in Schema.GetProperty("required").EnumerateArray().Select(e => e.GetString()!))
            Assert.True(Json.TryGetProperty(req, out _), $"Missing required top-level field '{req}'.");
    }

    [Fact]
    public void Evidence_Validator_Passes()
    {
        var errors = EvidenceValidator.Validate(Doc);
        Assert.True(errors.Count == 0, string.Join("; ", errors));
    }

    [Fact]
    public void Missing_Explicit_Arm64_Target_Does_Not_Claim_Incompatibility()
    {
        var build = Json.GetProperty("buildFindings");

        Assert.False(build.GetProperty("arm64TargetExists").GetBoolean());
        Assert.Contains(
            "does not establish incompatibility",
            build.GetProperty("evidence")[0].GetProperty("observation").GetString());
    }

    [Fact]
    public void Toplevel_Patterns_Hold()
    {
        AssertPattern(RootPattern("assessmentId"), Json.GetProperty("assessmentId").GetString()!);
        AssertPattern(RootPattern("generatedAt"), Json.GetProperty("generatedAt").GetString()!);

        var commitPattern = Schema.GetProperty("properties").GetProperty("repository")
            .GetProperty("properties").GetProperty("commitSha").GetProperty("pattern").GetString()!;
        AssertPattern(commitPattern, Json.GetProperty("repository").GetProperty("commitSha").GetString()!);
    }

    [Fact]
    public void Dependency_Enums_Are_Valid()
    {
        var types = DefEnum("DependencyFinding", "type");
        var statuses = DefEnum("DependencyFinding", "architectureStatus");
        var criticalities = DefEnum("DependencyFinding", "criticality");
        var arches = DefEnumItems("DependencyFinding", "availableArchitectures");

        foreach (var d in Json.GetProperty("dependencies").EnumerateArray())
        {
            Assert.Contains(d.GetProperty("type").GetString(), types);
            Assert.Contains(d.GetProperty("architectureStatus").GetString(), statuses);
            Assert.Contains(d.GetProperty("criticality").GetString(), criticalities);
            foreach (var a in d.GetProperty("availableArchitectures").EnumerateArray())
                Assert.Contains(a.GetString(), arches);
        }
    }

    [Fact]
    public void CodeFinding_Severities_Are_Valid()
    {
        var severities = DefEnum("CodeFinding", "severity");
        foreach (var c in Json.GetProperty("codeFindings").EnumerateArray())
            Assert.Contains(c.GetProperty("severity").GetString(), severities);
    }

    [Fact]
    public void CodeFindings_Include_Actionable_Arm64_Remediation()
    {
        var findings = Json.GetProperty("codeFindings").EnumerateArray().ToList();
        var simd = findings.Single(f => f.GetProperty("category").GetString() == "simd");
        var pointer = findings.Single(f => f.GetProperty("category").GetString() == "pointer-size");

        Assert.Contains("NEON", simd.GetProperty("description").GetString());
        Assert.Contains("SSE2NEON", simd.GetProperty("description").GetString());
        Assert.Contains("uintptr_t", pointer.GetProperty("description").GetString());

        Assert.Equal("_mm_add_epi16(...)", simd.GetProperty("evidence")[0]
            .GetProperty("observation").GetString());
    }

    [Fact]
    public void WindowsExperience_Enums_Are_Valid()
    {
        var ui = DefEnum("WindowsExperience", "uiTechnology");
        var acc = DefEnum("WindowsExperience", "accessibilityEvidence");
        var we = Json.GetProperty("windowsExperience");
        Assert.Contains(we.GetProperty("uiTechnology").GetString(), ui);
        Assert.Contains(we.GetProperty("accessibilityEvidence").GetString(), acc);
    }

    [Fact]
    public void Unknown_Areas_Are_Valid()
    {
        var areas = DefEnum("Unknown", "area");
        foreach (var u in Json.GetProperty("unknowns").EnumerateArray())
            Assert.Contains(u.GetProperty("area").GetString(), areas);
    }

    [Fact]
    public void Every_Evidence_Has_Exactly_One_Source()
    {
        var sourceTypes = DefEnum("Evidence", "sourceType");
        foreach (var ev in AllEvidence(Json))
        {
            bool hasPath = ev.TryGetProperty("path", out _);
            bool hasArtifact = ev.TryGetProperty("artifact", out _);
            Assert.True(hasPath ^ hasArtifact, "Evidence must have exactly one of path/artifact.");
            Assert.Contains(ev.GetProperty("sourceType").GetString(), sourceTypes);
        }
    }

    [Fact]
    public void Skill_Names_Match_Pattern()
    {
        var pattern = Schema.GetProperty("$defs").GetProperty("Skill")
            .GetProperty("properties").GetProperty("name").GetProperty("pattern").GetString()!;
        foreach (var s in Json.GetProperty("availableSkills").EnumerateArray())
            AssertPattern(pattern, s.GetProperty("name").GetString()!);
    }

    // ---- helpers ----

    private static IEnumerable<JsonElement> AllEvidence(JsonElement root)
    {
        foreach (var d in root.GetProperty("dependencies").EnumerateArray())
            foreach (var e in d.GetProperty("evidence").EnumerateArray()) yield return e;
        foreach (var c in root.GetProperty("codeFindings").EnumerateArray())
            foreach (var e in c.GetProperty("evidence").EnumerateArray()) yield return e;
        foreach (var e in root.GetProperty("buildFindings").GetProperty("evidence").EnumerateArray()) yield return e;
        foreach (var e in root.GetProperty("windowsExperience").GetProperty("evidence").EnumerateArray()) yield return e;
    }

    private static string RootPattern(string prop) =>
        Schema.GetProperty("properties").GetProperty(prop).GetProperty("pattern").GetString()!;

    private static HashSet<string> DefEnum(string def, string prop) =>
        Schema.GetProperty("$defs").GetProperty(def).GetProperty("properties").GetProperty(prop)
            .GetProperty("enum").EnumerateArray().Select(e => e.GetString()!).ToHashSet();

    private static HashSet<string> DefEnumItems(string def, string prop) =>
        Schema.GetProperty("$defs").GetProperty(def).GetProperty("properties").GetProperty(prop)
            .GetProperty("items").GetProperty("enum").EnumerateArray().Select(e => e.GetString()!).ToHashSet();

    private static void AssertPattern(string pattern, string value) =>
        Assert.True(Regex.IsMatch(value, pattern), $"Value '{value}' does not match pattern '{pattern}'.");

    private static ReadinessManifest BuildManifest()
    {
        var snapshot = new RepositorySnapshot
        {
            RunId = "run-abc123",
            RepoUrl = "https://github.com/example/demo-app",
            WorkspacePath = "/ws",
            LocalRepoPath = "/ws/repo",
            CommitSha = "0123456789abcdef0123456789abcdef01234567",
            DefaultBranch = "main",
            FileCount = 120
        };

        var m = new ReadinessManifest { Repository = snapshot, Target = MigrationTarget.Arm64Native };
        m.FilesScanned = 120;

        m.Technology = new TechnologyProfile
        {
            Languages = { new LanguageStat("C++", 40, 1000), new LanguageStat("Python", 30, 800) },
            Frameworks = { "electron" },
            ProjectTypes = { "desktop" },
            BuildSystems = { "CMake" },
            PackageManagers = { "npm", "pip" },
            Installers = { "msix" },
            HasExistingCi = true,
            CiSystems = { "github-actions" }
        };

        m.BuildReadiness = new BuildReadiness { TestsExist = true, DetectedTargets = { "Release|x64" } };
        m.WindowsExperience = new WindowsExperience { UiTechnology = "electron", InstallerExists = true, Evidence = { "electron detected" } };

        m.Dependencies =
        [
            new DependencyFinding { Id = FindingId.Compute("binary","a.dll","1"), Name = "a.dll", Source = "binary", Machine = "x64", Classification = DependencyClassification.EmulationOnly, EvidencePath = "bin/a.dll" },
            new DependencyFinding { Id = FindingId.Compute("npm","left-pad","2"), Name = "left-pad", Source = "npm", Classification = DependencyClassification.Arm64Ready, EvidencePath = "package.json" },
            new DependencyFinding { Id = FindingId.Compute("nuget","Foo","3"), Name = "Foo", Source = "NuGet", Classification = DependencyClassification.Unknown, EvidencePath = "packages.config" },
            new DependencyFinding { Id = FindingId.Compute("binary","d.sys","4"), Name = "d.sys", Source = "binary", Machine = "x64", Classification = DependencyClassification.Blocked, EvidencePath = "drv/d.sys" }
        ];

        m.ArchitectureFindings =
        [
            new ArchitectureFinding { Id = FindingId.Compute("simd","x.c","10"), Category = "x86/x64 SIMD intrinsics (SSE/AVX)", File = "x.c", Line = 10, Snippet = "_mm_add_epi16(...)", Severity = FindingSeverity.High },
            new ArchitectureFinding { Id = FindingId.Compute("ptr","y.c","20"), Category = "Pointer-size assumption", File = "y.c", Line = 20, Snippet = "(int)&p", Severity = FindingSeverity.Low }
        ];

        m.ScannersCompleted.AddRange(["technology-discovery", "build-readiness", "dependency-scan", "code-compatibility"]);
        m.Skills.Add(new SkillInfo("technology-discovery", "1.0.0", "Detects tech.", false, ["repository-snapshot"], ["technology"]));
        m.Skills.Add(new SkillInfo("dependency-scan", "1.0.0", "Scans deps.", false, ["repository-snapshot"], ["dependencies"]));

        return m;
    }
}
