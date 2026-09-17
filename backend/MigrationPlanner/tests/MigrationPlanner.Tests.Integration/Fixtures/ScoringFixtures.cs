using MigrationPlanner.Domain.Assessment;

namespace MigrationPlanner.Tests.Integration.Fixtures;

/// <summary>
/// The four end-to-end fixtures from IMPLEMENTATION_PLAN.md §9. Every fixture
/// is a self-contained <see cref="RepositoryAssessmentV1"/> shaped to exercise
/// a specific scoring outcome.
/// </summary>
internal static class ScoringFixtures
{
    public static RepositoryAssessmentV1 ReadyManagedApp() =>
        Base(
            assessmentId: "fixture-ready-managed-app",
            dependencies: new[]
            {
                Dep("dep-ready-01", "Sample.Utilities", ArchitectureStatus.Ready),
                Dep("dep-ready-02", "Sample.Serialization", ArchitectureStatus.Ready),
            },
            codeFindings: new[]
            {
                Code("code-info-01", Severity.Informational, "ARM-CODE-STYLE-01"),
            },
            build: Build("build-ready-01",
                arm64Target: true, arm64EcTarget: false, ci: true, packaging: true, tests: true,
                targets: new[] { "arm64/Release", "arm64/Debug" }),
            windows: Windows(UiTechnology.WinUi3,
                installer: true, offline: true, a11y: AccessibilityEvidenceLevel.Full,
                notifications: true, lifecycle: true),
            coverage: FullCoverage());

    public static RepositoryAssessmentV1 ModerateBuildGap() =>
        Base(
            assessmentId: "fixture-moderate-build-gap",
            dependencies: new[]
            {
                Dep("dep-mod-01", "Sample.Utilities", ArchitectureStatus.Ready),
                Dep("dep-mod-02", "Sample.Legacy", ArchitectureStatus.EmulationOnly,
                    criticality: Criticality.Optional),
            },
            codeFindings: new[]
            {
                Code("code-high-01", Severity.High, "ARM-CODE-XPLAT-01"),
                Code("code-med-01", Severity.Medium, "ARM-CODE-INTRINSIC-01"),
            },
            build: Build("build-mod-01",
                arm64Target: true, arm64EcTarget: false, ci: false, packaging: false, tests: true,
                targets: new[] { "arm64/Release" }),
            windows: Windows(UiTechnology.Wpf,
                installer: false, offline: false, a11y: AccessibilityEvidenceLevel.Partial,
                notifications: false, lifecycle: false),
            coverage: FullCoverage());

    public static RepositoryAssessmentV1 BlockedNativeApp() =>
        Base(
            assessmentId: "fixture-blocked-native-app",
            dependencies: new[]
            {
                Dep("dep-blk-01", "Acme.Native.X64", ArchitectureStatus.Blocked,
                    type: DependencyType.Native),
                Dep("dep-blk-02", "Sample.Utilities", ArchitectureStatus.Ready),
            },
            codeFindings: new[]
            {
                Code("code-crit-01", Severity.Critical, "ARM-CODE-SIMD-01"),
                Code("code-high-01", Severity.High, "ARM-CODE-XPLAT-01"),
            },
            build: Build("build-blk-01",
                arm64Target: false, arm64EcTarget: false, ci: false, packaging: false, tests: true,
                targets: new[] { "x64/Release" }),
            windows: Windows(UiTechnology.WinForms,
                installer: false, offline: false, a11y: AccessibilityEvidenceLevel.None,
                notifications: false, lifecycle: false),
            coverage: FullCoverage());

    public static RepositoryAssessmentV1 IncompleteAssessment() =>
        Base(
            assessmentId: "fixture-incomplete-assessment",
            dependencies: Array.Empty<DependencyFinding>(),
            codeFindings: Array.Empty<CodeFinding>(),
            build: Build("build-inc-01",
                arm64Target: false, arm64EcTarget: false, ci: false, packaging: false, tests: false,
                targets: Array.Empty<string>()),
            windows: Windows(UiTechnology.Unknown,
                installer: false, offline: false, a11y: AccessibilityEvidenceLevel.Unknown,
                notifications: false, lifecycle: false),
            coverage: new ScanCoverage(
                FilesScanned: 3,
                FilesTotal: 200,
                DependencyResolutionRate: 0.10,
                ScannersCompleted: new[] { "repo-discovery" },
                ScannersFailed: new[] { "dep-scanner", "code-scanner" }),
            unknowns: new[]
            {
                new Unknown("Dependency graph could not be resolved.", UnknownArea.Dependency,
                    EvidenceIds: Array.Empty<string>()),
                new Unknown("No code scan output.", UnknownArea.Code,
                    EvidenceIds: Array.Empty<string>()),
                new Unknown("Build configuration not discovered.", UnknownArea.Build,
                    EvidenceIds: Array.Empty<string>()),
            });

    // ----- shape helpers -----

    private static RepositoryAssessmentV1 Base(
        string assessmentId,
        IReadOnlyList<DependencyFinding> dependencies,
        IReadOnlyList<CodeFinding> codeFindings,
        BuildFindings build,
        WindowsExperience windows,
        ScanCoverage coverage,
        IReadOnlyList<Unknown>? unknowns = null) =>
        new(
            SchemaVersion: "1.0",
            AssessmentId: assessmentId,
            GeneratedAt: new DateTimeOffset(2026, 9, 16, 0, 0, 0, TimeSpan.Zero),
            Producer: new Producer(
                Name: "arm-migration-assist-feature1",
                Version: "0.1.0",
                Ruleset: null,
                ScannerVersions: Array.Empty<ScannerVersion>()),
            Repository: new Repository(
                Name: assessmentId,
                Url: $"https://example.com/org/{assessmentId}",
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
                CiSystems: new[] { "github-actions" }),
            Dependencies: dependencies,
            CodeFindings: codeFindings,
            BuildFindings: build,
            WindowsExperience: windows,
            ScanCoverage: coverage,
            Unknowns: unknowns ?? Array.Empty<Unknown>(),
            AvailableSkills: new[]
            {
                new Skill(
                    Name: "assessment/repository-discovery",
                    Version: "0.1.0",
                    Description: "Discovers repository structure and scanner inputs.",
                    WriteAccess: false,
                    SupportedInputs: new[] { "repository" },
                    SupportedOutputs: new[] { "assessment" }),
            });

    private static DependencyFinding Dep(
        string evidenceId,
        string name,
        ArchitectureStatus status,
        DependencyType type = DependencyType.Managed,
        Criticality criticality = Criticality.Required) =>
        new(
            EvidenceId: evidenceId,
            Name: name,
            Ecosystem: "nuget",
            Type: type,
            Criticality: criticality,
            ArchitectureStatus: status,
            AvailableArchitectures: status == ArchitectureStatus.Ready
                ? new[] { Architecture.AnyCpu }
                : new[] { Architecture.X64 },
            ReplacementCandidates: Array.Empty<string>(),
            Evidence: new[]
            {
                new Evidence(SourceType.Manifest, $"Dependency {name} discovered.", Path: "packages.lock.json"),
            },
            Confidence: 0.9);

    private static CodeFinding Code(string evidenceId, Severity severity, string ruleId) =>
        new(
            EvidenceId: evidenceId,
            RuleId: ruleId,
            Category: "arm",
            Severity: severity,
            File: "src/App.cs",
            Description: $"{severity} finding {ruleId}.",
            Evidence: new[]
            {
                new Evidence(SourceType.File, "Matching source line.", Path: "src/App.cs"),
            },
            Confidence: 0.8);

    private static BuildFindings Build(
        string evidenceId, bool arm64Target, bool arm64EcTarget, bool ci, bool packaging, bool tests,
        IReadOnlyList<string> targets) =>
        new(
            EvidenceId: evidenceId,
            Arm64TargetExists: arm64Target,
            Arm64EcTargetExists: arm64EcTarget,
            Arm64CiJobExists: ci,
            PackagingSupportsArm64: packaging,
            TestsExist: tests,
            DetectedTargets: targets,
            Evidence: new[]
            {
                new Evidence(SourceType.Manifest, "Build configuration inspected.", Path: "src/App.csproj"),
            });

    private static WindowsExperience Windows(
        UiTechnology ui, bool installer, bool offline, AccessibilityEvidenceLevel a11y,
        bool notifications, bool lifecycle) =>
        new(
            WindowsVersionExists: true,
            UiTechnology: ui,
            InstallerExists: installer,
            OfflineCapable: offline,
            AccessibilityEvidence: a11y,
            NotificationsIntegrated: notifications,
            LifecycleIntegrated: lifecycle,
            Evidence: new[]
            {
                new Evidence(SourceType.Manifest, $"UI technology {ui} detected.", Path: "src/App.xaml"),
            });

    private static ScanCoverage FullCoverage() =>
        new(
            FilesScanned: 200,
            FilesTotal: 200,
            DependencyResolutionRate: 1.0,
            ScannersCompleted: new[] { "repo-discovery", "dep-scanner", "code-scanner", "build-scanner" },
            ScannersFailed: Array.Empty<string>());
}
