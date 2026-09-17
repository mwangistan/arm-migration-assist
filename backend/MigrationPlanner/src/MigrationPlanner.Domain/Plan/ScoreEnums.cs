using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using MigrationPlanner.Domain.Assessment;

namespace MigrationPlanner.Domain.Plan;

[JsonConverter(typeof(EnumMemberJsonConverter<DimensionKey>))]
public enum DimensionKey
{
    [EnumMember(Value = "dependency-compatibility")] DependencyCompatibility,
    [EnumMember(Value = "code-compatibility")] CodeCompatibility,
    [EnumMember(Value = "build-and-ci-readiness")] BuildAndCiReadiness,
    [EnumMember(Value = "runtime-and-validation-evidence")] RuntimeAndValidationEvidence,
    [EnumMember(Value = "windows-experience-and-deployment")] WindowsExperienceAndDeployment,
}

[JsonConverter(typeof(EnumMemberJsonConverter<ReadinessBand>))]
public enum ReadinessBand
{
    [EnumMember(Value = "ready-or-minor-changes")] ReadyOrMinorChanges,
    [EnumMember(Value = "moderate-migration")] ModerateMigration,
    [EnumMember(Value = "significant-remediation")] SignificantRemediation,
    [EnumMember(Value = "blocked-or-major-redesign")] BlockedOrMajorRedesign,
    [EnumMember(Value = "insufficient-evidence")] InsufficientEvidence,
}

[JsonConverter(typeof(EnumMemberJsonConverter<ConfidenceLabel>))]
public enum ConfidenceLabel
{
    [EnumMember(Value = "high")] High,
    [EnumMember(Value = "medium")] Medium,
    [EnumMember(Value = "low")] Low,
}

[JsonConverter(typeof(EnumMemberJsonConverter<ProvisionalReason>))]
public enum ProvisionalReason
{
    [EnumMember(Value = "scan-coverage-low")] ScanCoverageLow,
    [EnumMember(Value = "dependency-resolution-low")] DependencyResolutionLow,
    [EnumMember(Value = "scanner-failed")] ScannerFailed,
    [EnumMember(Value = "dimension-missing-evidence")] DimensionMissingEvidence,
    [EnumMember(Value = "insufficient-signals")] InsufficientSignals,
    [EnumMember(Value = "other")] Other,
}

[JsonConverter(typeof(EnumMemberJsonConverter<CapId>))]
public enum CapId
{
    [EnumMember(Value = "required-unsupported-driver-le-30")] RequiredUnsupportedDriverLe30,
    [EnumMember(Value = "required-x64-only-native-le-40")] RequiredX64OnlyNativeLe40,
    [EnumMember(Value = "no-arm64-or-arm64ec-target-le-60")] NoArm64OrArm64EcTargetLe60,
}

[JsonConverter(typeof(EnumMemberJsonConverter<BlockerCategory>))]
public enum BlockerCategory
{
    [EnumMember(Value = "dependency")] Dependency,
    [EnumMember(Value = "code")] Code,
    [EnumMember(Value = "build")] Build,
    [EnumMember(Value = "runtime")] Runtime,
    [EnumMember(Value = "windows")] Windows,
    [EnumMember(Value = "coverage")] Coverage,
}
