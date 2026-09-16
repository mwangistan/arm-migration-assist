using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using ArmMigrationAssist.Assessment.CodeCompatibility;
using ArmMigrationAssist.Assessment.DependencyScanner;
using ArmMigrationAssist.RepositoryDiscovery.Models;
using ArmMigrationAssist.RepositoryDiscovery.Scanning;

namespace ArmMigrationAssist.RepositoryDiscovery;

public interface IRepositoryAssessmentService
{
    Task<RepositoryAssessment> DiscoverAsync(
        string source,
        CancellationToken cancellationToken = default);
}

public sealed class RepositoryDiscoveryService : IRepositoryAssessmentService
{
    private const string Ruleset = "repository-discovery-1.0";
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
        var scan = new RepositoryScanner().Scan(catalog, cancellationToken);
        var dependencyScan = new DependencyScanner().Scan(catalog.Files, cancellationToken);
        var codeFindings = new CodeCompatibilityScanner().Scan(catalog.Files, cancellationToken);

        var unknowns = new List<AssessmentUnknown>();

        var unresolvedDependencies = dependencyScan.Findings
            .Where(finding => finding.ArchitectureStatus == "unknown")
            .ToArray();
        if (unresolvedDependencies.Length > 0)
        {
            unknowns.Add(new(
                $"ARM64 availability could not be established from repository evidence for {unresolvedDependencies.Length} declared dependency or dependencies.",
                "dependency",
                null,
                unresolvedDependencies.Select(finding => finding.EvidenceId).Take(100).ToArray()));
        }

        if (dependencyScan.MalformedManifestPaths.Count > 0)
        {
            var displayedPath = dependencyScan.MalformedManifestPaths[0];
            var omittedCount = dependencyScan.MalformedManifestPaths.Count - 1;
            var omittedSuffix = omittedCount > 0 ? $" and {omittedCount} more" : string.Empty;
            unknowns.Add(new(
                $"{dependencyScan.MalformedManifestPaths.Count} dependency manifest(s) could not be parsed; dependency coverage is incomplete: {displayedPath}{omittedSuffix}.",
                "dependency",
                null,
                []));
        }

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
            AssessmentId: CreateAssessmentId(
                workspace.RepositoryUrl,
                workspace.CommitSha,
                Ruleset),
            GeneratedAt: DateTimeOffset.UtcNow,
            Producer: new AssessmentProducer(
                "arm-migration-assist-repository-discovery",
                ProducerVersion,
                Ruleset,
                [
                    new ScannerVersion("repository-intake", ProducerVersion),
                    new ScannerVersion("technology-discovery", ProducerVersion),
                    new ScannerVersion("dependency-scanner", ProducerVersion),
                    new ScannerVersion("code-compatibility-scanner", ProducerVersion),
                ]),
            Repository: new RepositoryIdentity(
                workspace.Name,
                workspace.RepositoryUrl,
                workspace.CommitSha,
                workspace.DefaultBranch,
                scan.License),
            Technology: scan.Technology,
            Dependencies: dependencyScan.Findings,
            CodeFindings: codeFindings,
            BuildFindings: scan.BuildFindings,
            WindowsExperience: scan.WindowsExperience,
            ScanCoverage: new ScanCoverage(
                catalog.TotalFiles - catalog.SkippedFiles,
                catalog.TotalFiles,
                dependencyScan.ResolutionRate,
                ["repository-intake", "technology-discovery", "dependency-scanner", "code-compatibility-scanner"],
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
                new AvailableSkill(
                    "assessment/dependency-scanner",
                    ProducerVersion,
                    "Inventories declared dependencies and classifies repository-visible architecture signals.",
                    false,
                    ["repository-file-catalog"],
                    ["dependency-findings"]),
                new AvailableSkill(
                    "assessment/code-compatibility-scanner",
                    ProducerVersion,
                    "Detects architecture-sensitive source patterns with file and line evidence.",
                    false,
                    ["repository-file-catalog"],
                    ["code-findings"]),
            ]);
    }

    private static string CreateAssessmentId(
        string repositoryUrl,
        string commitSha,
        string ruleset)
    {
        var identity = $"{repositoryUrl}\n{commitSha}\n{ruleset}";
        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(identity)))
            .ToLowerInvariant();
        return $"assessment-{hash[..24]}";
    }
}