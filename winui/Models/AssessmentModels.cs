using System.Text.Json.Serialization;

namespace ArmMigrationAssist.WinUI.Models;

public sealed class RepositoryAssessment
{
    [JsonPropertyName("assessmentId")] public string AssessmentId { get; set; } = "";
    [JsonPropertyName("generatedAt")] public string GeneratedAt { get; set; } = "";
    [JsonPropertyName("repository")] public RepositoryInfo Repository { get; set; } = new();
    [JsonPropertyName("technology")] public TechnologyInfo Technology { get; set; } = new();
    [JsonPropertyName("dependencies")] public List<DependencyFinding> Dependencies { get; set; } = [];
    [JsonPropertyName("codeFindings")] public List<CodeFinding> CodeFindings { get; set; } = [];
    [JsonPropertyName("buildFindings")] public BuildFindings BuildFindings { get; set; } = new();
    [JsonPropertyName("scanCoverage")] public ScanCoverage ScanCoverage { get; set; } = new();
    [JsonPropertyName("unknowns")] public List<UnknownFinding> Unknowns { get; set; } = [];
}

public sealed class RepositoryInfo
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("url")] public string Url { get; set; } = "";
    [JsonPropertyName("commitSha")] public string CommitSha { get; set; } = "";
    [JsonPropertyName("defaultBranch")] public string DefaultBranch { get; set; } = "";
}

public sealed class TechnologyInfo
{
    [JsonPropertyName("languages")] public List<string> Languages { get; set; } = [];
    [JsonPropertyName("frameworks")] public List<string> Frameworks { get; set; } = [];
    [JsonPropertyName("buildSystems")] public List<string> BuildSystems { get; set; } = [];
    [JsonPropertyName("packageManagers")] public List<string> PackageManagers { get; set; } = [];
    [JsonPropertyName("installers")] public List<string> Installers { get; set; } = [];
    [JsonPropertyName("ciSystems")] public List<string> CiSystems { get; set; } = [];

    [JsonIgnore]
    public string Summary => string.Join(
        "  •  ",
        Languages.Concat(Frameworks).Concat(BuildSystems).Distinct(StringComparer.OrdinalIgnoreCase));
}

public sealed class Evidence
{
    [JsonPropertyName("path")] public string? Path { get; set; }
    [JsonPropertyName("artifact")] public string? Artifact { get; set; }
    [JsonPropertyName("observation")] public string Observation { get; set; } = "";
}

public sealed class DependencyFinding
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("version")] public string? Version { get; set; }
    [JsonPropertyName("ecosystem")] public string Ecosystem { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("architectureStatus")] public string ArchitectureStatus { get; set; } = "";
    [JsonPropertyName("availableArchitectures")] public List<string> AvailableArchitectures { get; set; } = [];
    [JsonPropertyName("evidence")] public List<Evidence> Evidence { get; set; } = [];
    [JsonPropertyName("confidence")] public double Confidence { get; set; }

    [JsonIgnore] public string DisplayVersion => Version ?? "Version unresolved";
    [JsonIgnore] public string Architectures => AvailableArchitectures.Count == 0 ? "Not reported" : string.Join(", ", AvailableArchitectures);
    [JsonIgnore] public string EvidenceSummary => Evidence.FirstOrDefault()?.Observation ?? "No observation";
    [JsonIgnore] public string ConfidencePercent => $"{Confidence:P0}";
}

public sealed class CodeFinding
{
    [JsonPropertyName("ruleId")] public string RuleId { get; set; } = "";
    [JsonPropertyName("category")] public string Category { get; set; } = "";
    [JsonPropertyName("severity")] public string Severity { get; set; } = "";
    [JsonPropertyName("file")] public string File { get; set; } = "";
    [JsonPropertyName("line")] public int? Line { get; set; }
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    [JsonPropertyName("evidence")] public List<Evidence> Evidence { get; set; } = [];

    [JsonIgnore] public string Location => Line is null ? File : $"{File}:{Line}";
    [JsonIgnore] public string EvidenceSummary => Evidence.FirstOrDefault()?.Observation ?? "File-level finding";
}

public sealed class BuildFindings
{
    [JsonPropertyName("arm64TargetExists")] public bool Arm64TargetExists { get; set; }
    [JsonPropertyName("arm64EcTargetExists")] public bool Arm64EcTargetExists { get; set; }
    [JsonPropertyName("arm64CiJobExists")] public bool Arm64CiJobExists { get; set; }
    [JsonPropertyName("packagingSupportsArm64")] public bool PackagingSupportsArm64 { get; set; }
    [JsonPropertyName("testsExist")] public bool TestsExist { get; set; }
}

public sealed class ScanCoverage
{
    [JsonPropertyName("filesScanned")] public int FilesScanned { get; set; }
    [JsonPropertyName("filesTotal")] public int FilesTotal { get; set; }
    [JsonPropertyName("dependencyResolutionRate")] public double DependencyResolutionRate { get; set; }
    [JsonPropertyName("scannersCompleted")] public List<string> ScannersCompleted { get; set; } = [];
    [JsonPropertyName("scannersFailed")] public List<string> ScannersFailed { get; set; } = [];
}

public sealed class UnknownFinding
{
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    [JsonPropertyName("area")] public string Area { get; set; } = "";
}
