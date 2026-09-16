using System.Text.Json.Serialization;

namespace ArmMigrationAssist.Api.Assessment.Contract;

// DTOs that serialize to exactly the RepositoryAssessmentV1.schema.json shape.
// Enum-valued fields are plain strings holding the exact schema tokens (e.g. "emulation-only")
// so we never fight C# enum naming. Null properties are omitted at serialization time, which
// is required for the Evidence path/artifact oneOf constraint.

public sealed class RepositoryAssessmentV1
{
    [JsonPropertyName("schemaVersion")] public string SchemaVersion { get; set; } = "1.0";
    [JsonPropertyName("assessmentId")] public required string AssessmentId { get; set; }
    [JsonPropertyName("generatedAt")] public required string GeneratedAt { get; set; }
    [JsonPropertyName("producer")] public required ProducerV1 Producer { get; set; }
    [JsonPropertyName("repository")] public required RepositoryV1 Repository { get; set; }
    [JsonPropertyName("technology")] public required TechnologyV1 Technology { get; set; }
    [JsonPropertyName("dependencies")] public List<DependencyFindingV1> Dependencies { get; set; } = [];
    [JsonPropertyName("codeFindings")] public List<CodeFindingV1> CodeFindings { get; set; } = [];
    [JsonPropertyName("buildFindings")] public required BuildFindingsV1 BuildFindings { get; set; }
    [JsonPropertyName("windowsExperience")] public required WindowsExperienceV1 WindowsExperience { get; set; }
    [JsonPropertyName("scanCoverage")] public required ScanCoverageV1 ScanCoverage { get; set; }
    [JsonPropertyName("unknowns")] public List<UnknownV1> Unknowns { get; set; } = [];
    [JsonPropertyName("availableSkills")] public List<SkillV1> AvailableSkills { get; set; } = [];
}

public sealed class ProducerV1
{
    [JsonPropertyName("name")] public required string Name { get; set; }
    [JsonPropertyName("version")] public required string Version { get; set; }
    [JsonPropertyName("ruleset")] public string? Ruleset { get; set; }
    [JsonPropertyName("scannerVersions")] public List<ScannerVersionV1>? ScannerVersions { get; set; }
}

public sealed class ScannerVersionV1
{
    [JsonPropertyName("name")] public required string Name { get; set; }
    [JsonPropertyName("version")] public required string Version { get; set; }
}

public sealed class RepositoryV1
{
    [JsonPropertyName("name")] public required string Name { get; set; }
    [JsonPropertyName("url")] public required string Url { get; set; }
    [JsonPropertyName("commitSha")] public required string CommitSha { get; set; }
    [JsonPropertyName("defaultBranch")] public required string DefaultBranch { get; set; }
    [JsonPropertyName("license")] public string? License { get; set; }
}

public sealed class TechnologyV1
{
    [JsonPropertyName("languages")] public List<string> Languages { get; set; } = [];
    [JsonPropertyName("frameworks")] public List<string> Frameworks { get; set; } = [];
    [JsonPropertyName("projectTypes")] public List<string> ProjectTypes { get; set; } = [];
    [JsonPropertyName("buildSystems")] public List<string> BuildSystems { get; set; } = [];
    [JsonPropertyName("packageManagers")] public List<string> PackageManagers { get; set; } = [];
    [JsonPropertyName("installers")] public List<string> Installers { get; set; } = [];
    [JsonPropertyName("ciSystems")] public List<string> CiSystems { get; set; } = [];
}

public sealed class EvidenceV1
{
    [JsonPropertyName("sourceType")] public required string SourceType { get; set; }
    [JsonPropertyName("path")] public string? Path { get; set; }
    [JsonPropertyName("artifact")] public string? Artifact { get; set; }
    [JsonPropertyName("observation")] public required string Observation { get; set; }
}

public sealed class DependencyFindingV1
{
    [JsonPropertyName("evidenceId")] public required string EvidenceId { get; set; }
    [JsonPropertyName("name")] public required string Name { get; set; }
    [JsonPropertyName("version")] public string? Version { get; set; }
    [JsonPropertyName("ecosystem")] public required string Ecosystem { get; set; }
    [JsonPropertyName("type")] public required string Type { get; set; }
    [JsonPropertyName("criticality")] public required string Criticality { get; set; }
    [JsonPropertyName("architectureStatus")] public required string ArchitectureStatus { get; set; }
    [JsonPropertyName("availableArchitectures")] public List<string> AvailableArchitectures { get; set; } = [];
    [JsonPropertyName("replacementCandidates")] public List<string> ReplacementCandidates { get; set; } = [];
    [JsonPropertyName("evidence")] public List<EvidenceV1> Evidence { get; set; } = [];
    [JsonPropertyName("confidence")] public required double Confidence { get; set; }
}

public sealed class CodeFindingV1
{
    [JsonPropertyName("evidenceId")] public required string EvidenceId { get; set; }
    [JsonPropertyName("ruleId")] public required string RuleId { get; set; }
    [JsonPropertyName("category")] public required string Category { get; set; }
    [JsonPropertyName("severity")] public required string Severity { get; set; }
    [JsonPropertyName("file")] public required string File { get; set; }
    [JsonPropertyName("line")] public int? Line { get; set; }
    [JsonPropertyName("column")] public int? Column { get; set; }
    [JsonPropertyName("description")] public required string Description { get; set; }
    [JsonPropertyName("evidence")] public List<EvidenceV1> Evidence { get; set; } = [];
    [JsonPropertyName("confidence")] public required double Confidence { get; set; }
}

public sealed class BuildFindingsV1
{
    [JsonPropertyName("evidenceId")] public required string EvidenceId { get; set; }
    [JsonPropertyName("arm64TargetExists")] public bool Arm64TargetExists { get; set; }
    [JsonPropertyName("arm64EcTargetExists")] public bool Arm64EcTargetExists { get; set; }
    [JsonPropertyName("arm64CiJobExists")] public bool Arm64CiJobExists { get; set; }
    [JsonPropertyName("packagingSupportsArm64")] public bool PackagingSupportsArm64 { get; set; }
    [JsonPropertyName("testsExist")] public bool TestsExist { get; set; }
    [JsonPropertyName("detectedTargets")] public List<string> DetectedTargets { get; set; } = [];
    [JsonPropertyName("evidence")] public List<EvidenceV1> Evidence { get; set; } = [];
}

public sealed class WindowsExperienceV1
{
    [JsonPropertyName("windowsVersionExists")] public bool WindowsVersionExists { get; set; }
    [JsonPropertyName("uiTechnology")] public string UiTechnology { get; set; } = "unknown";
    [JsonPropertyName("installerExists")] public bool InstallerExists { get; set; }
    [JsonPropertyName("offlineCapable")] public bool OfflineCapable { get; set; }
    [JsonPropertyName("accessibilityEvidence")] public string AccessibilityEvidence { get; set; } = "unknown";
    [JsonPropertyName("accessibilityNotes")] public string? AccessibilityNotes { get; set; }
    [JsonPropertyName("notificationsIntegrated")] public bool NotificationsIntegrated { get; set; }
    [JsonPropertyName("lifecycleIntegrated")] public bool LifecycleIntegrated { get; set; }
    [JsonPropertyName("evidence")] public List<EvidenceV1> Evidence { get; set; } = [];
}

public sealed class ScanCoverageV1
{
    [JsonPropertyName("filesScanned")] public int FilesScanned { get; set; }
    [JsonPropertyName("filesTotal")] public int FilesTotal { get; set; }
    [JsonPropertyName("dependencyResolutionRate")] public double DependencyResolutionRate { get; set; }
    [JsonPropertyName("scannersCompleted")] public List<string> ScannersCompleted { get; set; } = [];
    [JsonPropertyName("scannersFailed")] public List<string> ScannersFailed { get; set; } = [];
}

public sealed class UnknownV1
{
    [JsonPropertyName("description")] public required string Description { get; set; }
    [JsonPropertyName("area")] public required string Area { get; set; }
    [JsonPropertyName("requiredSkill")] public string? RequiredSkill { get; set; }
    [JsonPropertyName("evidenceIds")] public List<string>? EvidenceIds { get; set; }
}

public sealed class SkillV1
{
    [JsonPropertyName("name")] public required string Name { get; set; }
    [JsonPropertyName("version")] public required string Version { get; set; }
    [JsonPropertyName("description")] public required string Description { get; set; }
    [JsonPropertyName("writeAccess")] public bool WriteAccess { get; set; }
    [JsonPropertyName("supportedInputs")] public List<string> SupportedInputs { get; set; } = [];
    [JsonPropertyName("supportedOutputs")] public List<string> SupportedOutputs { get; set; } = [];
}
