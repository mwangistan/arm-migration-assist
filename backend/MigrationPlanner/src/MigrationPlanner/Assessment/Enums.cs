using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace MigrationPlanner.Assessment;

[JsonConverter(typeof(EnumMemberJsonConverter<SourceType>))]
public enum SourceType
{
    [EnumMember(Value = "file")] File,
    [EnumMember(Value = "manifest")] Manifest,
    [EnumMember(Value = "binary")] Binary,
    [EnumMember(Value = "config")] Config,
    [EnumMember(Value = "ci-log")] CiLog,
    [EnumMember(Value = "documentation")] Documentation,
    [EnumMember(Value = "artifact")] Artifact,
}

[JsonConverter(typeof(EnumMemberJsonConverter<DependencyType>))]
public enum DependencyType
{
    [EnumMember(Value = "managed")] Managed,
    [EnumMember(Value = "native")] Native,
    [EnumMember(Value = "com")] Com,
    [EnumMember(Value = "plugin")] Plugin,
    [EnumMember(Value = "driver")] Driver,
    [EnumMember(Value = "unknown")] Unknown,
}

[JsonConverter(typeof(EnumMemberJsonConverter<Criticality>))]
public enum Criticality
{
    [EnumMember(Value = "required")] Required,
    [EnumMember(Value = "optional")] Optional,
}

[JsonConverter(typeof(EnumMemberJsonConverter<ArchitectureStatus>))]
public enum ArchitectureStatus
{
    [EnumMember(Value = "ready")] Ready,
    [EnumMember(Value = "emulation-only")] EmulationOnly,
    [EnumMember(Value = "unknown")] Unknown,
    [EnumMember(Value = "blocked")] Blocked,
}

[JsonConverter(typeof(EnumMemberJsonConverter<Architecture>))]
public enum Architecture
{
    [EnumMember(Value = "x86")] X86,
    [EnumMember(Value = "x64")] X64,
    [EnumMember(Value = "arm")] Arm,
    [EnumMember(Value = "arm64")] Arm64,
    [EnumMember(Value = "arm64ec")] Arm64Ec,
    [EnumMember(Value = "any-cpu")] AnyCpu,
    [EnumMember(Value = "unknown")] Unknown,
}

[JsonConverter(typeof(EnumMemberJsonConverter<Severity>))]
public enum Severity
{
    [EnumMember(Value = "critical")] Critical,
    [EnumMember(Value = "high")] High,
    [EnumMember(Value = "medium")] Medium,
    [EnumMember(Value = "low")] Low,
    [EnumMember(Value = "informational")] Informational,
}

[JsonConverter(typeof(EnumMemberJsonConverter<UiTechnology>))]
public enum UiTechnology
{
    [EnumMember(Value = "winui3")] WinUi3,
    [EnumMember(Value = "wpf")] Wpf,
    [EnumMember(Value = "winforms")] WinForms,
    [EnumMember(Value = "uwp")] Uwp,
    [EnumMember(Value = "maui")] Maui,
    [EnumMember(Value = "xaml-islands")] XamlIslands,
    [EnumMember(Value = "qt")] Qt,
    [EnumMember(Value = "gtk")] Gtk,
    [EnumMember(Value = "electron")] Electron,
    [EnumMember(Value = "tauri")] Tauri,
    [EnumMember(Value = "flutter")] Flutter,
    [EnumMember(Value = "web")] Web,
    [EnumMember(Value = "cli")] Cli,
    [EnumMember(Value = "none")] None,
    [EnumMember(Value = "other")] Other,
    [EnumMember(Value = "unknown")] Unknown,
}

[JsonConverter(typeof(EnumMemberJsonConverter<AccessibilityEvidenceLevel>))]
public enum AccessibilityEvidenceLevel
{
    [EnumMember(Value = "none")] None,
    [EnumMember(Value = "partial")] Partial,
    [EnumMember(Value = "full")] Full,
    [EnumMember(Value = "unknown")] Unknown,
}

[JsonConverter(typeof(EnumMemberJsonConverter<UnknownArea>))]
public enum UnknownArea
{
    [EnumMember(Value = "repository")] Repository,
    [EnumMember(Value = "technology")] Technology,
    [EnumMember(Value = "dependency")] Dependency,
    [EnumMember(Value = "code")] Code,
    [EnumMember(Value = "build")] Build,
    [EnumMember(Value = "windows-experience")] WindowsExperience,
    [EnumMember(Value = "scan-coverage")] ScanCoverage,
    [EnumMember(Value = "other")] Other,
}
