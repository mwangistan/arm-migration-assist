using MigrationPlanner.Domain.Assessment;

namespace MigrationPlanner.Tests.Integration.Fixtures;

internal static class MinimalValidAssessmentFactory
{
    public static RepositoryAssessmentV1 Build(string? assessmentId = null)
    {
        var id = assessmentId ?? "assessment-integration-001";
        var evidence = new Evidence(
            SourceType: SourceType.File,
            Observation: "Detected sample project file.",
            Path: "src/App.csproj");

        return new RepositoryAssessmentV1(
            SchemaVersion: "1.0",
            AssessmentId: id,
            GeneratedAt: new DateTimeOffset(2026, 9, 15, 0, 0, 0, TimeSpan.Zero),
            Producer: new Producer(
                Name: "arm-migration-assist-feature1",
                Version: "0.1.0",
                Ruleset: null,
                ScannerVersions: Array.Empty<ScannerVersion>()),
            Repository: new Repository(
                Name: "sample-repo",
                Url: "https://example.com/org/sample-repo",
                CommitSha: "a1b2c3d4e5f60718293a4b5c6d7e8f9012345678",
                DefaultBranch: "main",
                License: null),
            Technology: new TechnologyInventory(
                Languages: new[] { "csharp" },
                Frameworks: Array.Empty<string>(),
                ProjectTypes: new[] { "desktop" },
                BuildSystems: new[] { "msbuild" },
                PackageManagers: new[] { "nuget" },
                Installers: Array.Empty<string>(),
                CiSystems: Array.Empty<string>()),
            Dependencies: new[]
            {
                new DependencyFinding(
                    EvidenceId: "dep-001",
                    Name: "Sample.Package",
                    Ecosystem: "nuget",
                    Type: DependencyType.Managed,
                    Criticality: Criticality.Optional,
                    ArchitectureStatus: ArchitectureStatus.Ready,
                    AvailableArchitectures: new[] { Architecture.AnyCpu },
                    ReplacementCandidates: Array.Empty<string>(),
                    Evidence: new[] { evidence },
                    Confidence: 0.9),
            },
            CodeFindings: new[]
            {
                new CodeFinding(
                    EvidenceId: "code-001",
                    RuleId: "ARM-CODE-SAMPLE-01",
                    Category: "sample",
                    Severity: Severity.Informational,
                    File: "src/App.cs",
                    Description: "Sample finding for integration test.",
                    Evidence: new[]
                    {
                        new Evidence(
                            SourceType: SourceType.File,
                            Observation: "Sample source line.",
                            Path: "src/App.cs"),
                    },
                    Confidence: 0.5),
            },
            BuildFindings: new BuildFindings(
                EvidenceId: "build-001",
                Arm64TargetExists: false,
                Arm64EcTargetExists: false,
                Arm64CiJobExists: false,
                PackagingSupportsArm64: false,
                TestsExist: true,
                DetectedTargets: new[] { "x64/Debug" },
                Evidence: new[]
                {
                    new Evidence(
                        SourceType: SourceType.Manifest,
                        Observation: "csproj declares only x64/Debug.",
                        Path: "src/App.csproj"),
                }),
            WindowsExperience: new WindowsExperience(
                WindowsVersionExists: true,
                UiTechnology: UiTechnology.Wpf,
                InstallerExists: false,
                OfflineCapable: false,
                AccessibilityEvidence: AccessibilityEvidenceLevel.Unknown,
                NotificationsIntegrated: false,
                LifecycleIntegrated: false,
                Evidence: new[]
                {
                    new Evidence(
                        SourceType: SourceType.Manifest,
                        Observation: "WPF entrypoint detected.",
                        Path: "src/App.xaml"),
                }),
            ScanCoverage: new ScanCoverage(
                FilesScanned: 10,
                FilesTotal: 10,
                DependencyResolutionRate: 1.0,
                ScannersCompleted: new[] { "dep", "code", "build" },
                ScannersFailed: Array.Empty<string>()),
            Unknowns: Array.Empty<Unknown>(),
            AvailableSkills: new[]
            {
                new Skill(
                    Name: "assessment/repository-discovery",
                    Version: "0.1.0",
                    Description: "Discovers repository structure and scanner inputs.",
                    WriteAccess: false,
                    SupportedInputs: new[] { "repository" },
                    SupportedOutputs: new[] { "assessment" }),
                new Skill(
                    Name: "build-config-generator",
                    Version: "1.0.0",
                    Description: "Generates reviewable ARM64 build configuration patches.",
                    WriteAccess: true,
                    SupportedInputs: new[] { "src/App.csproj" },
                    SupportedOutputs: new[] { "patch" }),
                new Skill(
                    Name: "ci-pipeline-generator",
                    Version: "1.0.0",
                    Description: "Generates reviewable ARM64 CI pipeline patches.",
                    WriteAccess: true,
                    SupportedInputs: new[] { "repository" },
                    SupportedOutputs: new[] { "patch" }),
                new Skill(
                    Name: "code-transformer",
                    Version: "1.0.0",
                    Description: "Generates approval-gated architecture compatibility patches.",
                    WriteAccess: true,
                    SupportedInputs: new[] { "src/App.cs" },
                    SupportedOutputs: new[] { "patch" }),
            });
    }
}
