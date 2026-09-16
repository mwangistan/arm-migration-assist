using System.Text.Json.Serialization;

namespace ArmMigrationAssist.RepositoryDiscovery.Models;

public sealed record RepositoryAssessment(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("assessmentId")] string AssessmentId,
    [property: JsonPropertyName("generatedAt")] DateTimeOffset GeneratedAt,
    [property: JsonPropertyName("producer")] AssessmentProducer Producer,
    [property: JsonPropertyName("repository")] RepositoryIdentity Repository,
    [property: JsonPropertyName("technology")] TechnologyInventory Technology,
    [property: JsonPropertyName("dependencies")] IReadOnlyList<object> Dependencies,
    [property: JsonPropertyName("codeFindings")] IReadOnlyList<object> CodeFindings,
    [property: JsonPropertyName("buildFindings")] BuildFindings BuildFindings,
    [property: JsonPropertyName("windowsExperience")] WindowsExperience WindowsExperience,
    [property: JsonPropertyName("scanCoverage")] ScanCoverage ScanCoverage,
    [property: JsonPropertyName("unknowns")] IReadOnlyList<AssessmentUnknown> Unknowns,
    [property: JsonPropertyName("availableSkills")] IReadOnlyList<AvailableSkill> AvailableSkills);

public sealed record AssessmentProducer(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("ruleset")] string Ruleset,
    [property: JsonPropertyName("scannerVersions")] IReadOnlyList<ScannerVersion> ScannerVersions);

public sealed record ScannerVersion(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("version")] string Version);

public sealed record RepositoryIdentity(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("commitSha")] string CommitSha,
    [property: JsonPropertyName("defaultBranch")] string DefaultBranch,
    [property: JsonPropertyName("license")] string? License);

public sealed record TechnologyInventory(
    [property: JsonPropertyName("languages")] IReadOnlyList<string> Languages,
    [property: JsonPropertyName("frameworks")] IReadOnlyList<string> Frameworks,
    [property: JsonPropertyName("projectTypes")] IReadOnlyList<string> ProjectTypes,
    [property: JsonPropertyName("buildSystems")] IReadOnlyList<string> BuildSystems,
    [property: JsonPropertyName("packageManagers")] IReadOnlyList<string> PackageManagers,
    [property: JsonPropertyName("installers")] IReadOnlyList<string> Installers,
    [property: JsonPropertyName("ciSystems")] IReadOnlyList<string> CiSystems);

public sealed record BuildFindings(
    [property: JsonPropertyName("evidenceId")] string EvidenceId,
    [property: JsonPropertyName("arm64TargetExists")] bool Arm64TargetExists,
    [property: JsonPropertyName("arm64EcTargetExists")] bool Arm64EcTargetExists,
    [property: JsonPropertyName("arm64CiJobExists")] bool Arm64CiJobExists,
    [property: JsonPropertyName("packagingSupportsArm64")] bool PackagingSupportsArm64,
    [property: JsonPropertyName("testsExist")] bool TestsExist,
    [property: JsonPropertyName("detectedTargets")] IReadOnlyList<string> DetectedTargets,
    [property: JsonPropertyName("evidence")] IReadOnlyList<Evidence> Evidence);

public sealed record WindowsExperience(
    [property: JsonPropertyName("windowsVersionExists")] bool WindowsVersionExists,
    [property: JsonPropertyName("uiTechnology")] string UiTechnology,
    [property: JsonPropertyName("installerExists")] bool InstallerExists,
    [property: JsonPropertyName("offlineCapable")] bool OfflineCapable,
    [property: JsonPropertyName("accessibilityEvidence")] string AccessibilityEvidence,
    [property: JsonPropertyName("accessibilityNotes")] string? AccessibilityNotes,
    [property: JsonPropertyName("notificationsIntegrated")] bool NotificationsIntegrated,
    [property: JsonPropertyName("lifecycleIntegrated")] bool LifecycleIntegrated,
    [property: JsonPropertyName("evidence")] IReadOnlyList<Evidence> Evidence);

public sealed record Evidence(
    [property: JsonPropertyName("sourceType")] string SourceType,
    [property: JsonPropertyName("path"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Path,
    [property: JsonPropertyName("artifact"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Artifact,
    [property: JsonPropertyName("observation")] string Observation);

public sealed record ScanCoverage(
    [property: JsonPropertyName("filesScanned")] int FilesScanned,
    [property: JsonPropertyName("filesTotal")] int FilesTotal,
    [property: JsonPropertyName("dependencyResolutionRate")] decimal DependencyResolutionRate,
    [property: JsonPropertyName("scannersCompleted")] IReadOnlyList<string> ScannersCompleted,
    [property: JsonPropertyName("scannersFailed")] IReadOnlyList<string> ScannersFailed);

public sealed record AssessmentUnknown(
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("area")] string Area,
    [property: JsonPropertyName("requiredSkill")] string? RequiredSkill,
    [property: JsonPropertyName("evidenceIds")] IReadOnlyList<string> EvidenceIds);

public sealed record AvailableSkill(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("writeAccess")] bool WriteAccess,
    [property: JsonPropertyName("supportedInputs")] IReadOnlyList<string> SupportedInputs,
    [property: JsonPropertyName("supportedOutputs")] IReadOnlyList<string> SupportedOutputs);