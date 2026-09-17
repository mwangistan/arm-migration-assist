using MigrationPlanner.Domain.Assessment;

namespace MigrationPlanner.Tests.Unit.Scoring;

/// <summary>
/// Focused builder for scorer unit tests. Starts from a perfectly ready
/// assessment (ARM64 targets, tests, WinUI 3, full coverage) so each test
/// can mutate exactly the fields it cares about.
/// </summary>
internal static class ScoringAssessmentBuilder
{
    public static RepositoryAssessmentV1 Ready(
        IReadOnlyList<DependencyFinding>? dependencies = null,
        IReadOnlyList<CodeFinding>? codeFindings = null,
        BuildFindings? build = null,
        WindowsExperience? windows = null,
        ScanCoverage? coverage = null,
        IReadOnlyList<Unknown>? unknowns = null,
        IReadOnlyList<string>? languages = null,
        IReadOnlyList<string>? frameworks = null,
        string assessmentId = "assessment-scorer-tests")
    {
        var evidence = new[]
        {
            new Evidence(SourceType.File, "sample observation", Path: "src/App.csproj"),
        };

        return new RepositoryAssessmentV1(
            SchemaVersion: "1.0",
            AssessmentId: assessmentId,
            GeneratedAt: new DateTimeOffset(2026, 9, 16, 0, 0, 0, TimeSpan.Zero),
            Producer: new Producer("test-producer", "0.1.0"),
            Repository: new Repository("sample", "https://example.com/sample",
                "a1b2c3d4e5f60718293a4b5c6d7e8f9012345678", "main"),
            Technology: new TechnologyInventory(
                Languages: languages ?? new[] { "csharp" },
                Frameworks: frameworks ?? Array.Empty<string>(),
                ProjectTypes: new[] { "desktop" },
                BuildSystems: new[] { "msbuild" },
                PackageManagers: new[] { "nuget" },
                Installers: Array.Empty<string>(),
                CiSystems: Array.Empty<string>()),
            Dependencies: dependencies ?? Array.Empty<DependencyFinding>(),
            CodeFindings: codeFindings ?? Array.Empty<CodeFinding>(),
            BuildFindings: build ?? new BuildFindings(
                EvidenceId: "build-001",
                Arm64TargetExists: true,
                Arm64EcTargetExists: false,
                Arm64CiJobExists: true,
                PackagingSupportsArm64: true,
                TestsExist: true,
                DetectedTargets: new[] { "arm64/Release" },
                Evidence: evidence),
            WindowsExperience: windows ?? new WindowsExperience(
                WindowsVersionExists: true,
                UiTechnology: UiTechnology.WinUi3,
                InstallerExists: true,
                OfflineCapable: true,
                AccessibilityEvidence: AccessibilityEvidenceLevel.Full,
                NotificationsIntegrated: true,
                LifecycleIntegrated: true,
                Evidence: evidence),
            ScanCoverage: coverage ?? new ScanCoverage(
                FilesScanned: 100,
                FilesTotal: 100,
                DependencyResolutionRate: 1.0,
                ScannersCompleted: new[] { "dep", "code", "build" },
                ScannersFailed: Array.Empty<string>()),
            Unknowns: unknowns ?? Array.Empty<Unknown>(),
            AvailableSkills: Array.Empty<Skill>());
    }

    public static DependencyFinding Dep(
        string evidenceId,
        string name,
        DependencyType type = DependencyType.Managed,
        Criticality criticality = Criticality.Required,
        ArchitectureStatus status = ArchitectureStatus.Ready,
        IReadOnlyList<string>? replacements = null,
        double confidence = 0.9) =>
        new(
            EvidenceId: evidenceId,
            Name: name,
            Ecosystem: "nuget",
            Type: type,
            Criticality: criticality,
            ArchitectureStatus: status,
            AvailableArchitectures: Array.Empty<Architecture>(),
            ReplacementCandidates: replacements ?? Array.Empty<string>(),
            Evidence: new[] { new Evidence(SourceType.Manifest, "dep observation", Path: "dep.json") },
            Confidence: confidence);

    public static CodeFinding Code(
        string evidenceId,
        Severity severity,
        string ruleId = "ARM-CODE-SAMPLE",
        double confidence = 1.0) =>
        new(
            EvidenceId: evidenceId,
            RuleId: ruleId,
            Category: "arm",
            Severity: severity,
            File: "src/App.cs",
            Description: $"{severity} finding",
            Evidence: new[] { new Evidence(SourceType.File, "line", Path: "src/App.cs") },
            Confidence: confidence);
}
