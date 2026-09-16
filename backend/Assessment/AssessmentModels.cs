using System.Security.Cryptography;
using System.Text;

namespace ArmMigrationAssist.Api.Assessment;

/// <summary>Requested migration target for an assessment run.</summary>
public enum MigrationTarget
{
    Arm64Native,
    Arm64EC
}

/// <summary>ARM64 readiness classification for a single dependency (Story 1.3).</summary>
public enum DependencyClassification
{
    Arm64Ready,
    EmulationOnly,
    Unknown,
    Blocked
}

/// <summary>Severity of an architecture-compatibility finding (Story 1.4).</summary>
public enum FindingSeverity
{
    Info,
    Low,
    Medium,
    High
}

/// <summary>Request body for POST /assess and the CLI.</summary>
public sealed record AssessmentRequest(string RepoUrl, MigrationTarget Target = MigrationTarget.Arm64Native);

/// <summary>Result of Story 1.1 - Repository Ingestion.</summary>
public sealed class RepositorySnapshot
{
    public required string RunId { get; init; }
    public required string RepoUrl { get; init; }
    public required string WorkspacePath { get; init; }
    public required string LocalRepoPath { get; init; }
    public string? CommitSha { get; init; }
    public string? DefaultBranch { get; init; }
    public int FileCount { get; init; }
    public DateTimeOffset ClonedAt { get; init; }
    public bool FromCache { get; init; }
}

/// <summary>Result of Story 1.2 - Technology Stack Discovery.</summary>
public sealed class TechnologyProfile
{
    public List<LanguageStat> Languages { get; init; } = [];
    public List<string> Frameworks { get; init; } = [];
    public List<string> ProjectTypes { get; init; } = [];
    public List<string> BuildSystems { get; init; } = [];
    public List<string> PackageManagers { get; init; } = [];
    public List<string> Installers { get; init; } = [];
    public List<string> ProjectFiles { get; init; } = [];
    public List<string> EntryPoints { get; init; } = [];
    public bool HasExistingCi { get; init; }
    public List<string> CiSystems { get; init; } = [];
}

public sealed record LanguageStat(string Language, int FileCount, long Bytes);

/// <summary>
/// Build-target readiness signals (plan Section 5 "Build Scanner"). Consumed by Feature 2
/// Story 2.1 to compute a build-readiness score. Facts only - no scoring here.
/// </summary>
public sealed class BuildReadiness
{
    public bool HasArm64BuildTarget { get; set; }
    public bool HasArm64EcTarget { get; set; }
    public bool CiHasArm64Job { get; set; }
    public bool PackagingSupportsArm64 { get; set; }
    public bool TestsExist { get; set; }
    public List<string> DetectedTargets { get; init; } = [];
    public List<string> RuntimeIdentifiers { get; init; } = [];
    public List<string> Evidence { get; init; } = [];
}

/// <summary>Windows-native experience signals (schema windowsExperience).</summary>
public sealed class WindowsExperience
{
    public bool WindowsVersionExists { get; set; }
    public string UiTechnology { get; set; } = "unknown";
    public bool InstallerExists { get; set; }
    public bool OfflineCapable { get; set; }
    public string AccessibilityEvidence { get; set; } = "unknown";
    public string? AccessibilityNotes { get; set; }
    public bool NotificationsIntegrated { get; set; }
    public bool LifecycleIntegrated { get; set; }
    public List<string> Evidence { get; init; } = [];
}

/// <summary>Metadata describing a registered assessment skill (schema availableSkills).</summary>
public sealed record SkillInfo(
    string Name,
    string Version,
    string Description,
    bool WriteAccess,
    IReadOnlyList<string> SupportedInputs,
    IReadOnlyList<string> SupportedOutputs);

/// <summary>A single entry in the dependency compatibility matrix (Story 1.3).</summary>
public sealed record DependencyFinding
{
    public string Id { get; init; } = string.Empty;
    public required string Name { get; init; }
    public string? Version { get; init; }
    public required string Source { get; init; } // NuGet, npm, vcpkg, binary
    public string? Machine { get; init; }         // for inspected binaries: ARM64, x64, x86
    public required DependencyClassification Classification { get; init; }
    public required string EvidencePath { get; init; }
    public string? Notes { get; init; }

    /// <summary>True when the dependency is a development-only dependency (schema criticality: optional).</summary>
    public bool? IsDevelopment { get; init; }

    /// <summary>True for repository-declared dependencies; false for transitive graph dependencies.</summary>
    public bool IsDirect { get; init; } = true;

    /// <summary>Architectures known to be available (registry-verified), schema availableArchitectures tokens.</summary>
    public IReadOnlyList<string>? AvailableArchitectures { get; init; }

    /// <summary>True when the classification was confirmed against a package registry (not a heuristic).</summary>
    public bool RegistryVerified { get; init; }
}

/// <summary>A single architecture-specific code blocker (Story 1.4).</summary>
public sealed class ArchitectureFinding
{
    public string Id { get; init; } = string.Empty;
    public required string Category { get; init; }
    public required string File { get; init; }
    public int Line { get; init; }
    public string? Snippet { get; init; }
    public required FindingSeverity Severity { get; init; }
}

/// <summary>The full evidence-based manifest - the contract Feature 2 consumes.</summary>
public sealed class ReadinessManifest
{
    public const string SchemaVersion = "1.0";

    public string Schema { get; init; } = SchemaVersion;
    public required RepositorySnapshot Repository { get; init; }
    public required MigrationTarget Target { get; init; }
    public TechnologyProfile Technology { get; set; } = new();
    public BuildReadiness BuildReadiness { get; set; } = new();
    public WindowsExperience WindowsExperience { get; set; } = new();
    public List<DependencyFinding> Dependencies { get; set; } = [];
    public List<ArchitectureFinding> ArchitectureFindings { get; set; } = [];

    // Assessment run diagnostics (schema scanCoverage / availableSkills).
    public int FilesScanned { get; set; }
    public List<string> ScannersCompleted { get; } = [];
    public List<string> ScannersFailed { get; } = [];
    public List<SkillInfo> Skills { get; } = [];

    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>Deterministic, stable finding identifiers so Feature 2 can reference and dedupe.</summary>
public static class FindingId
{
    public static string Compute(params string[] parts)
    {
        var joined = string.Join("|", parts).ToLowerInvariant();
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(joined));
        return Convert.ToHexString(bytes)[..12].ToLowerInvariant();
    }
}
