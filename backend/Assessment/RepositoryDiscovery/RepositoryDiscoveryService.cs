using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using ArmMigrationAssist.Assessment.CodeCompatibility;
using ArmMigrationAssist.Assessment.DependencyScanner;
using ArmMigrationAssist.RepositoryDiscovery.Models;
using ArmMigrationAssist.RepositoryDiscovery.Jobs;
using ArmMigrationAssist.RepositoryDiscovery.GitHub;
using ArmMigrationAssist.RepositoryDiscovery.Scanning;
using ArmMigrationAssist.RepositoryDiscovery.Skills;
using ArmMigrationAssist.RepositoryDiscovery.Validation;
using ArmMigrationAssist.RepositoryWorkspace;

namespace ArmMigrationAssist.RepositoryDiscovery;

public interface IRepositoryAssessmentService
{
    Task<RepositoryAssessment> DiscoverAsync(
        string source,
    CancellationToken cancellationToken = default,
        RepositoryAccessOptions? accessOptions = null,
        IProgress<AssessmentProgress>? progress = null);
}

public sealed record RepositoryAccessOptions(bool UseStoredGitHubCredentials = false);

public sealed class RepositoryDiscoveryService : IRepositoryAssessmentService
{
    private const string Ruleset = "repository-discovery-1.2";
    private static readonly string ProducerVersion = ResolveProducerVersion();
    private readonly IGitHubRepositorySource gitHubRepositorySource;
    private readonly IGitHubMetadataResolver? metadataResolver;
    private readonly IRepositoryClonePool? clonePool;

    public RepositoryDiscoveryService()
        : this(new GitHubRepositorySource())
    {
    }

    // Used by the composed host: an anonymous URL assessment shares a clone with the same
    // (URL, SHA) key that a later F3 migration job requests, so the demo path materializes
    // the working copy once instead of once per feature.
    public RepositoryDiscoveryService(IRepositoryClonePool clonePool)
        : this(new GitHubRepositorySource())
    {
        this.clonePool = clonePool;
        this.metadataResolver = (IGitHubMetadataResolver)this.gitHubRepositorySource;
    }

    internal RepositoryDiscoveryService(IGitHubRepositorySource gitHubRepositorySource)
    {
        this.gitHubRepositorySource = gitHubRepositorySource;
        this.metadataResolver = gitHubRepositorySource as IGitHubMetadataResolver;
    }

    internal RepositoryDiscoveryService(
        IGitHubRepositorySource gitHubRepositorySource,
        IRepositoryClonePool? clonePool,
        IGitHubMetadataResolver? metadataResolver)
    {
        this.gitHubRepositorySource = gitHubRepositorySource;
        this.clonePool = clonePool;
        this.metadataResolver = metadataResolver ?? gitHubRepositorySource as IGitHubMetadataResolver;
    }

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
        CancellationToken cancellationToken = default,
        RepositoryAccessOptions? accessOptions = null,
        IProgress<AssessmentProgress>? progress = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        progress?.Report(new AssessmentProgress("repository-intake", 5, "Opening the repository."));
        var git = new GitClient(accessOptions?.UseStoredGitHubCredentials == true);
        using var workspace = await new RepositoryIntake(
            git,
            gitHubRepositorySource,
            accessOptions?.UseStoredGitHubCredentials == true,
            clonePool,
            metadataResolver)
            .OpenAsync(source, cancellationToken);
        progress?.Report(new AssessmentProgress("file-catalog", 20, "Cataloging tracked repository files."));
        var catalog = await RepositoryFileCatalog.CreateAsync(
            workspace.RootPath,
            git,
            cancellationToken,
            workspace.KnownRelativePaths,
            workspace.KnownTotalFiles,
            workspace.KnownSkippedFiles);
        progress?.Report(new AssessmentProgress(
            "parallel-scanning",
            40,
            "Analyzing technology, dependencies, and architecture-sensitive code in parallel."));
        var completedScanners = 0;
        var technologyTask = RunScannerAsync(
            "technology-discovery",
            "Technology and build signal analysis completed.",
            () => new RepositoryScanner().Scan(catalog, cancellationToken));
        var dependencyTask = RunScannerAsync(
            "dependency-scanner",
            "Dependency manifest and binary analysis completed.",
            () => new DependencyScanner().Scan(catalog.Files, cancellationToken));
        var codeTask = RunScannerAsync(
            "code-compatibility-scanner",
            "Architecture-sensitive code analysis completed.",
            () => new CodeCompatibilityScanner().Scan(catalog.Files, cancellationToken));
        await Task.WhenAll(technologyTask, dependencyTask, codeTask);
        var scan = await technologyTask;
        var dependencyScan = await dependencyTask;
        var codeFindings = await codeTask;
        progress?.Report(new AssessmentProgress("finalizing", 95, "Validating assessment evidence."));

        async Task<T> RunScannerAsync<T>(string phase, string message, Func<T> scanOperation)
        {
            var result = await Task.Run(scanOperation, cancellationToken);
            var completed = Interlocked.Increment(ref completedScanners);
            progress?.Report(new AssessmentProgress(phase, 40 + (completed * 16), message));
            return result;
        }

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

        var availableSkills = BuildAvailableSkills(catalog, codeFindings);

        var assessment = new RepositoryAssessment(
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
            AvailableSkills: availableSkills);

        RepositoryAssessmentValidator.EnsureValid(assessment);
        return assessment;
    }

    private static IReadOnlyList<AvailableSkill> BuildAvailableSkills(
        RepositoryFileCatalog catalog,
        IReadOnlyList<CodeFinding> codeFindings)
    {
        // Every candidate skill uses the catalog's namespaced name. We then keep
        // only those the catalog marks 'runnable'. Skills declared here but not
        // runnable in the catalog would let the planner emit work items no
        // runner in this repo can execute; drop them at the source.
        var candidates = new List<AvailableSkill>
        {
                new AvailableSkill(
                    "assessment/repository-discovery",
                    ProducerVersion,
                    "Discovers repository identity and static technology, build, CI, and Windows experience signals.",
                    false,
                    ["github-url", "local-git-repository"],
                    ["repository-assessment-v1", "technology-inventory"]),
                new AvailableSkill(
                    "assessment/dependency-scan",
                    ProducerVersion,
                    "Inventories declared dependencies and classifies repository-visible architecture signals.",
                    false,
                    ["repository-file-catalog"],
                    ["dependency-findings"]),
                new AvailableSkill(
                    "assessment/code-compatibility-scan",
                    ProducerVersion,
                    "Detects architecture-sensitive source patterns with file and line evidence.",
                    false,
                    ["repository-file-catalog"],
                    ["code-findings"]),
        };
        var buildInputs = catalog.Files
            .Where(file => IsSupportedBuildInput(file.RelativePath))
            .Select(file => file.RelativePath)
            .Distinct(StringComparer.Ordinal)
            .Take(50)
            .ToArray();
        if (buildInputs.Length > 0)
        {
            candidates.Add(
                new AvailableSkill(
                    "build/add-arm64-target",
                    ProducerVersion,
                    "Generates reviewable ARM64 changes for supported Docker, .NET, and Visual C++ build files.",
                    true,
                    buildInputs,
                    ["patch"]));
        }

        candidates.Add(
                new AvailableSkill(
                    "pipeline/github-actions-arm64-job",
                    ProducerVersion,
                    "Generates reviewable ARM64 GitHub Actions or Azure Pipelines changes.",
                    true,
                    ["repository"],
                    ["patch"]));
        var codeInputs = codeFindings
            .Select(finding => finding.File)
            .Distinct(StringComparer.Ordinal)
            .Take(50)
            .ToArray();
        if (codeInputs.Length > 0)
        {
            candidates.Add(
                new AvailableSkill(
                    "code/arch-conditional-cleanup",
                    ProducerVersion,
                    "Generates approval-gated architecture compatibility patches for identified source files.",
                    true,
                    codeInputs,
                    ["patch"]));
        }

        // Python-specific audit skills. Declared only when the repo actually has
        // pip manifests / Python source, so F2 doesn't cite them against a
        // non-Python repo.
        var pipManifests = catalog.Files
            .Where(file => IsPipManifest(file.RelativePath))
            .Select(file => file.RelativePath)
            .Distinct(StringComparer.Ordinal)
            .Take(10)
            .ToArray();
        var pyFiles = catalog.Files
            .Where(file => file.RelativePath.EndsWith(".py", StringComparison.OrdinalIgnoreCase))
            .Select(file => file.RelativePath)
            .Distinct(StringComparer.Ordinal)
            .Take(50)
            .ToArray();
        var hasRequirementsTxt = catalog.Files.Any(file =>
            Path.GetFileName(file.RelativePath).StartsWith("requirements", StringComparison.OrdinalIgnoreCase)
            && file.RelativePath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase));
        if (pipManifests.Length > 0)
        {
            candidates.Add(new AvailableSkill(
                "python/pytorch-arm64-wheel-audit",
                ProducerVersion,
                "Audits torch/torchvision/torchaudio pins and reports Windows-on-Arm wheel status without modifying manifests.",
                true,
                pipManifests,
                ["report"]));
            candidates.Add(new AvailableSkill(
                "python/native-wheel-audit",
                ProducerVersion,
                "Classifies pip dependencies against a hand-curated native-package table and emits an ARM64 wheel-verification checklist.",
                true,
                pipManifests,
                ["report"]));
        }
        if (pyFiles.Length > 0)
        {
            candidates.Add(new AvailableSkill(
                "python/cuda-to-directml-audit",
                ProducerVersion,
                "Enumerates every CUDA reference in Python source and emits a routing report; does not choose a backend.",
                true,
                pyFiles,
                ["report"]));
        }
        if (hasRequirementsTxt)
        {
            candidates.Add(new AvailableSkill(
                "python/pip-constraints-arm64-scaffold",
                ProducerVersion,
                "Emits a starter constraints-arm64.txt with blank pins plus a README. Skipped if constraints-arm64.txt already exists.",
                true,
                ["requirements.txt"],
                ["patch", "report"]));
        }

        var runnable = SkillCatalog.Default;
        return candidates
            .Where(skill => runnable.IsRunnable(skill.Name))
            .ToList();
    }

    private static bool IsPipManifest(string path)
    {
        var name = Path.GetFileName(path);
        return (name.StartsWith("requirements", StringComparison.OrdinalIgnoreCase)
                && name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            || name.Equals("pyproject.toml", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSupportedBuildInput(string path)
    {
        var name = Path.GetFileName(path);
        return name.Equals("Dockerfile", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".vcxproj", StringComparison.OrdinalIgnoreCase);
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