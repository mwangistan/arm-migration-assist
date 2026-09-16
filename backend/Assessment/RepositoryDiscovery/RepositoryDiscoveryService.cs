using System.Reflection;
using ArmMigrationAssist.RepositoryDiscovery.Models;
using ArmMigrationAssist.RepositoryDiscovery.Scanning;

namespace ArmMigrationAssist.RepositoryDiscovery;

public sealed class RepositoryDiscoveryService
{
    private static readonly string ProducerVersion = ResolveProducerVersion();

    private static string ResolveProducerVersion()
    {
        var assembly = typeof(RepositoryDiscoveryService).Assembly;
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            return informationalVersion;
        }

        var assemblyVersion = assembly.GetName().Version;
        return assemblyVersion is null
            ? "1.0.0"
            : $"{assemblyVersion.Major}.{assemblyVersion.Minor}.{Math.Max(assemblyVersion.Build, 0)}";
    }

    public async Task<RepositoryAssessment> DiscoverAsync(
        string source,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        var git = new GitClient();
        using var workspace = await new RepositoryIntake(git)
            .OpenAsync(source, cancellationToken);
        var catalog = await RepositoryFileCatalog.CreateAsync(
            workspace.RootPath,
            git,
            cancellationToken);
        var scan = new RepositoryScanner().Scan(catalog);

        var unknowns = new List<AssessmentUnknown>
        {
            new(
                "Dependency ARM64 compatibility is outside repository discovery and has not been assessed.",
                "dependency",
                "assessment/dependency-scanner",
                []),
            new(
                "Architecture-specific source patterns are outside repository discovery and have not been assessed.",
                "code",
                "assessment/code-compatibility-scanner",
                []),
        };

        if (!scan.OfflineCapabilityEstablished)
        {
            unknowns.Add(new(
                "Meaningful offline capability could not be established from static repository signals.",
                "windows-experience",
                null,
                []));
        }

        if (scan.WindowsExperience.AccessibilityEvidence == "unknown")
        {
            unknowns.Add(new(
                "Accessibility coverage could not be established from static repository signals.",
                "windows-experience",
                null,
                []));
        }

        if (catalog.SkippedFiles > 0)
        {
            unknowns.Add(new(
                $"{catalog.SkippedFiles} tracked file(s) were not scanned because they were unsafe, missing, or exceeded scan limits.",
                "scan-coverage",
                null,
                []));
        }

        return new RepositoryAssessment(
            SchemaVersion: "1.0",
            AssessmentId: $"assessment-{Guid.NewGuid():N}",
            GeneratedAt: DateTimeOffset.UtcNow,
            Producer: new AssessmentProducer(
                "arm-migration-assist-repository-discovery",
                ProducerVersion,
                "repository-discovery-1.0",
                [
                    new ScannerVersion("repository-intake", ProducerVersion),
                    new ScannerVersion("technology-discovery", ProducerVersion),
                ]),
            Repository: new RepositoryIdentity(
                workspace.Name,
                workspace.RepositoryUrl,
                workspace.CommitSha,
                workspace.DefaultBranch,
                scan.License),
            Technology: scan.Technology,
            Dependencies: [],
            CodeFindings: [],
            BuildFindings: scan.BuildFindings,
            WindowsExperience: scan.WindowsExperience,
            ScanCoverage: new ScanCoverage(
                catalog.TotalFiles - catalog.SkippedFiles,
                catalog.TotalFiles,
                0m,
                ["repository-intake", "technology-discovery"],
                []),
            Unknowns: unknowns,
            AvailableSkills:
            [
                new AvailableSkill(
                    "assessment/repository-discovery",
                    ProducerVersion,
                    "Discovers repository identity and static technology, build, CI, and Windows experience signals.",
                    false,
                    ["github-url", "local-git-repository"],
                    ["repository-assessment-v1", "technology-inventory"]),
            ]);
    }
}