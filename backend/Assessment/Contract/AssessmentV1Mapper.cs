using System.Text.Json;
using System.Text.Json.Serialization;

namespace ArmMigrationAssist.Api.Assessment.Contract;

/// <summary>
/// Maps the internal <see cref="ReadinessManifest"/> onto the wire contract
/// <see cref="RepositoryAssessmentV1"/> defined by RepositoryAssessmentV1.schema.json.
/// Also owns the serializer options that guarantee schema-shaped JSON (camelCase already
/// baked into the DTO via [JsonPropertyName]; nulls omitted for the Evidence path/artifact
/// oneOf constraint).
/// </summary>
public static class AssessmentV1Mapper
{
    public const string ProducerName = "arm-migration-assist-feature1";
    public const string ProducerVersion = "1.0.0";

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Serialize(RepositoryAssessmentV1 doc) => JsonSerializer.Serialize(doc, JsonOptions);

    public static RepositoryAssessmentV1 Map(ReadinessManifest m)
    {
        var repo = m.Repository;
        var deps = m.Dependencies.Select(MapDependency).ToList();
        var code = m.ArchitectureFindings.Select(MapCode).ToList();

        int classified = m.Dependencies.Count(d => d.Classification != DependencyClassification.Unknown);
        double resolutionRate = m.Dependencies.Count == 0 ? 1.0 : (double)classified / m.Dependencies.Count;

        return new RepositoryAssessmentV1
        {
            SchemaVersion = "1.0",
            AssessmentId = repo.RunId,
            GeneratedAt = m.GeneratedAt.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'"),
            Producer = new ProducerV1
            {
                Name = ProducerName,
                Version = ProducerVersion,
                ScannerVersions = m.Skills
                    .Select(s => new ScannerVersionV1 { Name = s.Name, Version = s.Version })
                    .ToList()
            },
            Repository = new RepositoryV1
            {
                Name = DeriveName(repo.RepoUrl),
                Url = repo.RepoUrl,
                CommitSha = repo.CommitSha ?? new string('0', 40),
                DefaultBranch = repo.DefaultBranch ?? "unknown",
                License = repo.License
            },
            Technology = new TechnologyV1
            {
                Languages = m.Technology.Languages.Select(l => l.Language).Distinct().ToList(),
                Frameworks = m.Technology.Frameworks,
                ProjectTypes = m.Technology.ProjectTypes,
                BuildSystems = m.Technology.BuildSystems,
                PackageManagers = m.Technology.PackageManagers.Select(NormalizeEcosystem).Distinct().ToList(),
                Installers = m.Technology.Installers,
                CiSystems = m.Technology.CiSystems
            },
            Dependencies = deps,
            CodeFindings = code,
            BuildFindings = MapBuild(m.BuildReadiness),
            WindowsExperience = MapWindows(m.WindowsExperience),
            ScanCoverage = new ScanCoverageV1
            {
                FilesScanned = Math.Min(m.FilesScanned, repo.FileCount),
                FilesTotal = repo.FileCount,
                DependencyResolutionRate = Math.Round(resolutionRate, 4),
                ScannersCompleted = m.ScannersCompleted.ToList(),
                ScannersFailed = m.ScannersFailed.ToList()
            },
            Unknowns = BuildUnknowns(m, deps),
            AvailableSkills = m.Skills.Select(MapSkill).Concat(RemediationSkills).ToList()
        };
    }

    /// <summary>
    /// Remediation skills owned by the downstream automation (Feature 3 generators in
    /// AutomatedMigration/Program.cs). Feature 1 only runs read-only scanners, but the planner
    /// assigns migration work items to these write-capable skills, so they must appear in
    /// availableSkills — otherwise the planner cites a skill it can neither find here nor omit,
    /// and rejects its own plan. Names must match the generator keys the executor dispatches on.
    /// </summary>
    private static readonly SkillV1[] RemediationSkills =
    [
        new SkillV1
        {
            Name = "build-config-generator",
            Version = "1.0.0",
            Description = "Adds ARM64/win-arm64 build targets and configurations to Dockerfiles, MSBuild projects, and native (.vcxproj) projects.",
            WriteAccess = true,
            SupportedInputs = ["Dockerfile", "csproj", "vcxproj"],
            SupportedOutputs = ["build-config-diff"]
        },
        new SkillV1
        {
            Name = "ci-pipeline-generator",
            Version = "1.0.0",
            Description = "Adds ARM64 build/test jobs to CI pipelines (e.g. GitHub Actions workflows) so ARM64 regressions are caught.",
            WriteAccess = true,
            SupportedInputs = ["ci-workflow"],
            SupportedOutputs = ["ci-workflow-diff"]
        },
        new SkillV1
        {
            Name = "code-transformer",
            Version = "1.0.0",
            Description = "Applies source-level ARM64 remediations such as adding architecture branches or replacing x86/x64 SIMD intrinsics with portable equivalents.",
            WriteAccess = true,
            SupportedInputs = ["source-file"],
            SupportedOutputs = ["source-diff"]
        }
    ];

    private static DependencyFindingV1 MapDependency(DependencyFinding d)
    {
        var hasPath = !string.IsNullOrWhiteSpace(d.EvidencePath);
        var ev = new EvidenceV1
        {
            SourceType = d.Source switch
            {
                "binary" => "binary",
                "com" => "file",
                _ => "manifest"
            },
            // The evidence oneOf requires EXACTLY ONE of path/artifact. When the scanner did not
            // capture a source-tree path, fall back to an artifact identifier so the item stays valid.
            Path = hasPath ? d.EvidencePath : null,
            Artifact = hasPath ? null : $"{NormalizeEcosystem(d.Source)}:{d.Name}",
            Observation = d.Machine != null
                ? $"PE machine header of '{d.Name}' is {d.Machine}."
                : d.Notes ?? $"{d.Source} dependency '{d.Name}' declared."
        };

        return new DependencyFindingV1
        {
            EvidenceId = $"dep-{d.Id}",
            Name = d.Name,
            Version = d.Version,
            Ecosystem = NormalizeEcosystem(d.Source),
            Type = DependencyType(d),
            Criticality = d.IsDevelopment == true ? "optional" : "required",
            ArchitectureStatus = ArchStatus(d.Classification),
            AvailableArchitectures = d.AvailableArchitectures is { Count: > 0 } av
                ? av.ToList()
                : AvailableArchitectures(d.Machine),
            ReplacementCandidates = [],
            Evidence = [ev],
            Confidence = Confidence(d)
        };
    }

    private static double Confidence(DependencyFinding d)
    {
        if (d.Source == "binary") return 0.99;                                  // PE machine header is authoritative
        if (d.RegistryVerified)                                                 // confirmed against the registry
            return d.Classification == DependencyClassification.Unknown ? 0.6 : 0.9;
        return d.Classification == DependencyClassification.Unknown ? 0.5 : 0.7; // resolved package, heuristic arch
    }

    private static CodeFindingV1 MapCode(ArchitectureFinding f) => new()
    {
        EvidenceId = $"code-{f.Id}",
        RuleId = RuleId(f.Category),
        Category = CodeCategory(f.Category),
        Severity = Severity(f.Severity),
        File = f.File,
        Line = f.Line > 0 ? f.Line : null,
        Description = CodeDescription(f.Category),
        Evidence =
        [
            new EvidenceV1
            {
                SourceType = string.IsNullOrWhiteSpace(f.File) ? "artifact" : "file",
                Path = string.IsNullOrWhiteSpace(f.File) ? null : f.File,
                Artifact = string.IsNullOrWhiteSpace(f.File) ? $"code:{RuleId(f.Category)}" : null,
                Observation = string.IsNullOrWhiteSpace(f.Snippet) ? f.Category : f.Snippet!
            }
        ],
        Confidence = 0.8
    };

    private static string CodeDescription(string category) => category switch
    {
        "x86/x64 SIMD intrinsics (SSE/AVX)" =>
            "x86/x64 SSE or AVX intrinsics require an ARM64 implementation. Candidate approaches: replace them with equivalent ARM NEON intrinsics, use a portability layer such as SSE2NEON where its semantics match, or refactor to portable code that the compiler can auto-vectorize. Validate correctness and performance on ARM64 hardware.",
        "Inline assembly" or "Assembly source file" =>
            "Architecture-specific assembly requires an ARM64 path. Prefer portable C/C++ or compiler intrinsics; otherwise isolate the code behind architecture guards and provide an ARM64 assembly implementation with equivalent calling-convention and memory-ordering behavior.",
        "x86/x64 architecture assumption" =>
            "The code assumes an x86/x64 build. Add an ARM64 branch (for example, _M_ARM64 where appropriate), prefer capability checks over processor-name checks, and retain a portable fallback.",
        "P/Invoke to native library" =>
            "Verify that the imported native library ships a Windows ARM64 or Arm64EC-compatible binary with the expected exports and calling convention. Package architecture-specific native assets and select the matching binary at runtime.",
        "Pointer-size assumption" =>
            "Replace fixed-width pointer casts or offsets with pointer-sized types such as size_t, uintptr_t, INT_PTR, or UINT_PTR. Recheck structure layout, serialization, and interop boundaries on 64-bit targets.",
        _ => category
    };

    // Evidence arrays are capped by the wire contract (RepositoryAssessmentV1.schema.json):
    // buildFindings/windowsExperience allow at most 40 items. Scanners can emit hundreds of
    // near-duplicate observations (one per project/target), which both breaks maxItems and
    // bloats the planner request past its token budget, so dedupe then truncate.
    private const int MaxSectionEvidence = 40;

    private static BuildFindingsV1 MapBuild(BuildReadiness b)
    {
        var evidence = b.Evidence.Count > 0
            ? b.Evidence
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxSectionEvidence)
                .Select(e => new EvidenceV1
                {
                    SourceType = "config",
                    Artifact = "build-config",
                    Observation = e
                }).ToList()
            : [new EvidenceV1
            {
                SourceType = "artifact",
                Artifact = "build",
                Observation = "No explicit ARM64 build target was detected in project or CI files. " +
                              "This does not establish incompatibility: portable source and header-only libraries may build for ARM64 without architecture-specific configuration."
            }];

        return new BuildFindingsV1
        {
            EvidenceId = "build-findings",
            Arm64TargetExists = b.HasArm64BuildTarget,
            Arm64EcTargetExists = b.HasArm64EcTarget,
            Arm64CiJobExists = b.CiHasArm64Job,
            PackagingSupportsArm64 = b.PackagingSupportsArm64,
            TestsExist = b.TestsExist,
            DetectedTargets = b.DetectedTargets,
            Evidence = evidence
        };
    }

    private static WindowsExperienceV1 MapWindows(WindowsExperience w)
    {
        var evidence = w.Evidence.Count > 0
            ? w.Evidence
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxSectionEvidence)
                .Select(e => new EvidenceV1
                {
                    SourceType = "artifact",
                    Artifact = "windows-experience",
                    Observation = e
                }).ToList()
            : [new EvidenceV1 { SourceType = "artifact", Artifact = "windows-experience", Observation = "Windows-native experience not assessed by Feature 1." }];

        return new WindowsExperienceV1
        {
            WindowsVersionExists = w.WindowsVersionExists,
            UiTechnology = w.UiTechnology,
            InstallerExists = w.InstallerExists,
            OfflineCapable = w.OfflineCapable,
            AccessibilityEvidence = w.AccessibilityEvidence,
            AccessibilityNotes = w.AccessibilityNotes,
            NotificationsIntegrated = w.NotificationsIntegrated,
            LifecycleIntegrated = w.LifecycleIntegrated,
            Evidence = evidence
        };
    }

    private static SkillV1 MapSkill(SkillInfo s) => new()
    {
        Name = s.Name,
        Version = s.Version,
        Description = s.Description,
        WriteAccess = s.WriteAccess,
        SupportedInputs = s.SupportedInputs.ToList(),
        SupportedOutputs = s.SupportedOutputs.ToList()
    };

    private static List<UnknownV1> BuildUnknowns(ReadinessManifest m, List<DependencyFindingV1> deps)
    {
        var unknowns = new List<UnknownV1>();

        foreach (var d in deps.Where(d => d.ArchitectureStatus == "unknown"))
            unknowns.Add(d.Type == "com"
                ? new UnknownV1
                {
                    Description = $"COM component '{d.Name}' is activated in-process; an ARM64 (arm64 CLSID) server registration must be confirmed or it is an ARM64 blocker.",
                    Area = "dependency",
                    RequiredSkill = "com-arm64-verification",
                    EvidenceIds = [d.EvidenceId]
                }
                : new UnknownV1
                {
                    Description = $"ARM64 availability of dependency '{d.Name}' ({d.Ecosystem}) could not be resolved.",
                    Area = "dependency",
                    RequiredSkill = "dependency-registry-verification",
                    EvidenceIds = [d.EvidenceId]
                });

        if (!m.ScannersCompleted.Contains("component-detection") &&
            m.Technology.PackageManagers.Any(p => p.StartsWith("pip", StringComparison.OrdinalIgnoreCase)))
            unknowns.Add(new UnknownV1
            {
                Description = "Python (pip) dependencies were detected but not individually classified; native wheel ARM64 availability is unresolved.",
                Area = "dependency",
                RequiredSkill = "python-dependency-scan"
            });

        if (m.WindowsExperience.UiTechnology == "unknown")
            unknowns.Add(new UnknownV1
            {
                Description = "Windows UI technology could not be determined from the technology profile.",
                Area = "windows-experience",
                RequiredSkill = "windows-experience"
            });

        return unknowns;
    }

    private static string DeriveName(string url)
    {
        var trimmed = url.TrimEnd('/');
        var seg = trimmed[(trimmed.LastIndexOf('/') + 1)..];
        return string.IsNullOrEmpty(seg) ? trimmed : seg.Replace(".git", "");
    }

    private static string NormalizeEcosystem(string source) => source.ToLowerInvariant() switch
    {
        "nuget" => "nuget",
        "npm" => "npm",
        "vcpkg" => "vcpkg",
        "pip" or "pip/poetry" or "pypi" => "pypi",
        "crates.io" or "cargo" => "cargo",
        "go" or "golang" => "go",
        "maven" or "gradle" => "maven",
        "gem" or "rubygems" => "gem",
        "cocoapods" => "cocoapods",
        "conda" => "conda",
        "com" => "com",
        "binary" => "native-dll",
        _ => source.ToLowerInvariant()
    };

    private static string DependencyType(DependencyFinding d)
    {
        if (d.Source == "com") return "com";
        if (d.Source == "binary")
            return d.EvidencePath.EndsWith(".sys", StringComparison.OrdinalIgnoreCase) ? "driver" : "native";
        return NormalizeEcosystem(d.Source) switch
        {
            "npm" or "nuget" or "pypi" or "maven" or "gem" or "conda" => "managed",
            "vcpkg" or "cargo" or "go" or "cocoapods" => "native",
            _ => "unknown"
        };
    }

    private static string ArchStatus(DependencyClassification c) => c switch
    {
        DependencyClassification.Arm64Ready => "ready",
        DependencyClassification.EmulationOnly => "emulation-only",
        DependencyClassification.Blocked => "blocked",
        _ => "unknown"
    };

    private static List<string> AvailableArchitectures(string? machine) => machine switch
    {
        "ARM64" => ["arm64"],
        "x64" => ["x64"],
        "x86" => ["x86"],
        "ARM32" => ["arm"],
        _ => []
    };

    private static string Severity(FindingSeverity s) => s switch
    {
        FindingSeverity.High => "high",
        FindingSeverity.Medium => "medium",
        FindingSeverity.Low => "low",
        _ => "informational"
    };

    private static string CodeCategory(string category) => category switch
    {
        var c when c.Contains("SIMD", StringComparison.OrdinalIgnoreCase) => "simd",
        var c when c.Contains("assembly", StringComparison.OrdinalIgnoreCase) => "inline-asm",
        var c when c.Contains("P/Invoke", StringComparison.OrdinalIgnoreCase) => "p-invoke",
        var c when c.Contains("Pointer", StringComparison.OrdinalIgnoreCase) => "pointer-size",
        _ => "arch-assumption"
    };

    private static string RuleId(string category) => "ARM-CODE-" + CodeCategory(category).ToUpperInvariant();
}
