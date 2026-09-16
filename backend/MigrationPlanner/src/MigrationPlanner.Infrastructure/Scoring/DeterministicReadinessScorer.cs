using System.Reflection;
using System.Runtime.Serialization;
using MigrationPlanner.Application.Abstractions;
using MigrationPlanner.Domain.Assessment;
using MigrationPlanner.Domain.Plan;

namespace MigrationPlanner.Infrastructure.Scoring;

/// <summary>
/// Rules-driven, evidence-linked readiness scorer. Pure: same
/// <see cref="RepositoryAssessmentV1"/> input produces byte-identical output.
/// The formulas, deductions, cap thresholds, banding, and provisional rules
/// are documented in
/// <c>backend/MigrationPlanner/docs/decisions/0001-deterministic-scoring.md</c>
/// (ruleset id <c>scoring-v1</c>). Any change to the numbers MUST bump
/// <see cref="ScoringRuleset.RulesetId"/>.
/// </summary>
public sealed class DeterministicReadinessScorer : IReadinessScorer
{
    private static readonly ScoreProducer Producer = new(
        Name: ScoringRuleset.ScorerName,
        Version: ScoringRuleset.ScorerVersion,
        Ruleset: ScoringRuleset.RulesetId);

    public ReadinessScoreV1 Score(RepositoryAssessmentV1 assessment)
    {
        ArgumentNullException.ThrowIfNull(assessment);

        var dep = ScoreDependency(assessment);
        var code = ScoreCode(assessment);
        var build = ScoreBuild(assessment);
        var run = ScoreRuntime(assessment);
        var win = ScoreWindows(assessment);

        var dimensions = new[] { dep.Dimension, code.Dimension, build.Dimension, run, win };

        var uncappedRaw = dimensions.Sum(d => d.WeightedContribution);
        var uncapped = Math.Clamp((int)Math.Round(uncappedRaw, MidpointRounding.AwayFromZero), 0, 100);

        var caps = BuildCaps(dep, build);
        var overall = uncapped;
        foreach (var cap in caps)
        {
            overall = Math.Min(overall, cap.Ceiling);
        }
        overall = Math.Clamp(overall, 0, 100);

        var (provisional, provisionalReasons) = ProvisionalState(assessment);

        var confidenceRaw = dimensions.Sum(d => d.Confidence * d.WeightPct / 100.0);
        var confidenceScore = Math.Clamp(Math.Round(confidenceRaw, 2, MidpointRounding.AwayFromZero), 0.0, 1.0);
        var confidenceLabel = confidenceScore >= ScoringRuleset.ConfidenceHighMin
            ? ConfidenceLabel.High
            : confidenceScore >= ScoringRuleset.ConfidenceMediumMin
                ? ConfidenceLabel.Medium
                : ConfidenceLabel.Low;

        var band = ClassifyBand(assessment, overall, provisional);

        var majorBlockers = BuildMajorBlockers(assessment, dep, build, caps, code);

        var rationaleCodes = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var d in dimensions)
        {
            foreach (var c in d.RationaleCodes)
            {
                rationaleCodes.Add(c);
            }
        }
        foreach (var cap in caps)
        {
            rationaleCodes.Add("CAP-" + EnumMemberValue(cap.CapId).ToUpperInvariant());
        }

        var evidenceIds = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var d in dimensions)
        {
            foreach (var id in d.EvidenceIds)
            {
                evidenceIds.Add(id);
            }
        }
        foreach (var cap in caps)
        {
            foreach (var id in cap.TriggeredBy)
            {
                evidenceIds.Add(id);
            }
        }

        return new ReadinessScoreV1
        {
            SchemaVersion = "1.0",
            AssessmentId = assessment.AssessmentId,
            GeneratedAt = assessment.GeneratedAt,
            Producer = Producer,
            OverallScore = overall,
            UncappedScore = uncapped,
            Band = band,
            Confidence = confidenceLabel,
            ConfidenceScore = confidenceScore,
            Provisional = provisional,
            ProvisionalReasons = provisionalReasons,
            Dimensions = dimensions,
            CapsApplied = caps,
            MajorBlockers = majorBlockers,
            RationaleCodes = rationaleCodes.ToArray(),
            EvidenceIds = evidenceIds.ToArray(),
        };
    }

    // ---- Dimension 1: Dependency compatibility ----

    private sealed record DependencyResult(
        DimensionScore Dimension,
        IReadOnlyList<DependencyFinding> DriverBlockers,
        IReadOnlyList<DependencyFinding> NativeBlockersNoRepl);

    private static DependencyResult ScoreDependency(RepositoryAssessmentV1 a)
    {
        var deductions = new List<Deduction>();
        var codes = new SortedSet<string>(StringComparer.Ordinal);
        var evidenceIds = new SortedSet<string>(StringComparer.Ordinal);
        var driverBlockers = new List<DependencyFinding>();
        var nativeBlockersNoRepl = new List<DependencyFinding>();
        var confidences = new List<double>();

        foreach (var dep in a.Dependencies.OrderBy(d => d.EvidenceId, StringComparer.Ordinal))
        {
            var hasRepl = dep.ReplacementCandidates.Count > 0;
            int magnitude = 0;
            string? code = null;

            if (dep.Criticality == Criticality.Required)
            {
                switch (dep.ArchitectureStatus)
                {
                    case ArchitectureStatus.Blocked:
                        magnitude = hasRepl
                            ? ScoringRuleset.DepRequiredBlockedWithRepl
                            : ScoringRuleset.DepRequiredBlockedNoRepl;
                        code = hasRepl ? "DEP-BLOCKED-REQUIRED-WITH-REPL" : "DEP-BLOCKED-REQUIRED-NO-REPL";
                        break;
                    case ArchitectureStatus.EmulationOnly:
                        magnitude = ScoringRuleset.DepRequiredEmulation;
                        code = "DEP-EMULATION-REQUIRED";
                        break;
                    case ArchitectureStatus.Unknown:
                        magnitude = ScoringRuleset.DepRequiredUnknown;
                        code = "DEP-UNKNOWN-REQUIRED";
                        break;
                }
            }
            else
            {
                switch (dep.ArchitectureStatus)
                {
                    case ArchitectureStatus.Blocked:
                        magnitude = hasRepl
                            ? ScoringRuleset.DepOptionalBlockedWithRepl
                            : ScoringRuleset.DepOptionalBlockedNoRepl;
                        code = hasRepl ? "DEP-BLOCKED-OPTIONAL-WITH-REPL" : "DEP-BLOCKED-OPTIONAL";
                        break;
                    case ArchitectureStatus.EmulationOnly:
                        magnitude = ScoringRuleset.DepOptionalEmulation;
                        code = "DEP-EMULATION-OPTIONAL";
                        break;
                    case ArchitectureStatus.Unknown:
                        magnitude = ScoringRuleset.DepOptionalUnknown;
                        code = "DEP-UNKNOWN-OPTIONAL";
                        break;
                }
            }

            if (magnitude > 0 && code is not null)
            {
                deductions.Add(new Deduction(
                    Code: code,
                    Magnitude: magnitude,
                    Description: $"Dependency '{dep.Name}' is {EnumMemberValue(dep.ArchitectureStatus)} ({EnumMemberValue(dep.Criticality)}).",
                    EvidenceIds: new[] { dep.EvidenceId }));
                codes.Add(code);
            }

            evidenceIds.Add(dep.EvidenceId);
            confidences.Add(Math.Clamp(dep.Confidence, 0, 1));

            if (dep.Criticality == Criticality.Required && dep.ArchitectureStatus == ArchitectureStatus.Blocked)
            {
                if (dep.Type == DependencyType.Driver)
                {
                    driverBlockers.Add(dep);
                }
                if ((dep.Type == DependencyType.Native || dep.Type == DependencyType.Com) && !hasRepl)
                {
                    nativeBlockersNoRepl.Add(dep);
                }
            }
        }

        var rawScore = Math.Clamp(100 - deductions.Sum(d => d.Magnitude), 0, 100);
        var confidence = confidences.Count == 0 ? 0.60 : confidences.Average();
        if (a.Unknowns.Any(u => u.Area == UnknownArea.Dependency))
        {
            confidence -= 0.15;
        }
        confidence = Math.Clamp(confidence, 0, 1);

        var dimension = new DimensionScore(
            DimensionKey: DimensionKey.DependencyCompatibility,
            WeightPct: ScoringRuleset.WeightDependency,
            RawScore: rawScore,
            WeightedContribution: Math.Round(rawScore * ScoringRuleset.WeightDependency / 100.0, 2),
            Deductions: deductions,
            Confidence: Math.Round(confidence, 2),
            RationaleCodes: codes.ToArray(),
            EvidenceIds: evidenceIds.ToArray());

        return new DependencyResult(dimension, driverBlockers, nativeBlockersNoRepl);
    }

    // ---- Dimension 2: Code compatibility ----

    private sealed record CodeResult(DimensionScore Dimension, IReadOnlyList<CodeFinding> CriticalFindings);

    private static CodeResult ScoreCode(RepositoryAssessmentV1 a)
    {
        var deductions = new List<Deduction>();
        var codes = new SortedSet<string>(StringComparer.Ordinal);
        var evidenceIds = new SortedSet<string>(StringComparer.Ordinal);
        var confidences = new List<double>();
        var criticalFindings = new List<CodeFinding>();

        foreach (var finding in a.CodeFindings.OrderBy(f => f.EvidenceId, StringComparer.Ordinal))
        {
            var severityWeight = ScoringRuleset.CodeSeverityWeight(finding.Severity);
            var confidence = Math.Clamp(finding.Confidence, 0, 1);
            confidences.Add(confidence);
            evidenceIds.Add(finding.EvidenceId);

            if (severityWeight > 0)
            {
                var magnitude = (int)Math.Round(severityWeight * confidence, MidpointRounding.AwayFromZero);
                if (magnitude > 0)
                {
                    var code = $"CODE-{EnumMemberValue(finding.Severity).ToUpperInvariant()}-{SlugifyRuleId(finding.RuleId)}";
                    deductions.Add(new Deduction(
                        Code: code,
                        Magnitude: magnitude,
                        Description: $"{EnumMemberValue(finding.Severity)} code finding '{finding.RuleId}': {finding.Description}",
                        EvidenceIds: new[] { finding.EvidenceId }));
                    codes.Add(code);
                }
            }

            if (finding.Severity == Severity.Critical)
            {
                criticalFindings.Add(finding);
            }
        }

        var rawScore = Math.Clamp(100 - deductions.Sum(d => d.Magnitude), 0, 100);

        double confidenceValue;
        if (confidences.Count > 0)
        {
            confidenceValue = confidences.Average();
        }
        else
        {
            var codeScannerSeen = a.ScanCoverage.ScannersCompleted
                .Any(s => s.Contains("code", StringComparison.OrdinalIgnoreCase));
            confidenceValue = codeScannerSeen ? 0.70 : 0.40;
        }
        if (a.Unknowns.Any(u => u.Area == UnknownArea.Code))
        {
            confidenceValue -= 0.15;
        }
        confidenceValue = Math.Clamp(confidenceValue, 0, 1);

        var dimension = new DimensionScore(
            DimensionKey: DimensionKey.CodeCompatibility,
            WeightPct: ScoringRuleset.WeightCode,
            RawScore: rawScore,
            WeightedContribution: Math.Round(rawScore * ScoringRuleset.WeightCode / 100.0, 2),
            Deductions: deductions,
            Confidence: Math.Round(confidenceValue, 2),
            RationaleCodes: codes.ToArray(),
            EvidenceIds: evidenceIds.ToArray());

        return new CodeResult(dimension, criticalFindings);
    }

    // ---- Dimension 3: Build and CI ----

    private sealed record BuildResult(DimensionScore Dimension, bool NoArm64Target);

    private static BuildResult ScoreBuild(RepositoryAssessmentV1 a)
    {
        var deductions = new List<Deduction>();
        var codes = new SortedSet<string>(StringComparer.Ordinal);
        var b = a.BuildFindings;
        var evId = b.EvidenceId;
        var evArr = new[] { evId };

        var noAnyTarget = !b.Arm64TargetExists && !b.Arm64EcTargetExists;
        if (noAnyTarget)
        {
            deductions.Add(new Deduction("BUILD-NO-ARM64", ScoringRuleset.BuildNoArm64Target,
                "Neither an ARM64 nor an Arm64EC build target was detected.", evArr));
            codes.Add("BUILD-NO-ARM64");
        }
        if (!b.Arm64CiJobExists)
        {
            deductions.Add(new Deduction("BUILD-NO-ARM64-CI", ScoringRuleset.BuildNoArm64Ci,
                "No ARM64 CI job was detected.", evArr));
            codes.Add("BUILD-NO-ARM64-CI");
        }
        if (!b.PackagingSupportsArm64)
        {
            deductions.Add(new Deduction("BUILD-PKG-NO-ARM64", ScoringRuleset.BuildNoPackaging,
                "Packaging does not declare ARM64 support.", evArr));
            codes.Add("BUILD-PKG-NO-ARM64");
        }
        if (!b.TestsExist)
        {
            deductions.Add(new Deduction("BUILD-NO-TESTS", ScoringRuleset.BuildNoTests,
                "No tests were detected.", evArr));
            codes.Add("BUILD-NO-TESTS");
        }

        var rawScore = Math.Clamp(100 - deductions.Sum(d => d.Magnitude), 0, 100);

        var confidence = 0.90;
        if (a.Unknowns.Any(u => u.Area == UnknownArea.Build))
        {
            confidence = 0.60;
        }

        var dimension = new DimensionScore(
            DimensionKey: DimensionKey.BuildAndCiReadiness,
            WeightPct: ScoringRuleset.WeightBuild,
            RawScore: rawScore,
            WeightedContribution: Math.Round(rawScore * ScoringRuleset.WeightBuild / 100.0, 2),
            Deductions: deductions,
            Confidence: Math.Round(confidence, 2),
            RationaleCodes: codes.ToArray(),
            EvidenceIds: evArr);

        return new BuildResult(dimension, noAnyTarget);
    }

    // ---- Dimension 4: Runtime and validation ----

    private static DimensionScore ScoreRuntime(RepositoryAssessmentV1 a)
    {
        var deductions = new List<Deduction>();
        var codes = new SortedSet<string>(StringComparer.Ordinal);
        var evidenceIds = new SortedSet<string>(StringComparer.Ordinal) { a.BuildFindings.EvidenceId };

        if (!a.BuildFindings.TestsExist)
        {
            deductions.Add(new Deduction("RUN-NO-TESTS", ScoringRuleset.RuntimeNoTests,
                "Assessment reports no tests exist.", new[] { a.BuildFindings.EvidenceId }));
            codes.Add("RUN-NO-TESTS");
        }

        var totalFiles = Math.Max(1L, a.ScanCoverage.FilesTotal);
        var coverageRate = Math.Clamp(a.ScanCoverage.FilesScanned / (double)totalFiles, 0, 1);
        var coverageDeduction = (int)Math.Round((1 - coverageRate) * ScoringRuleset.RuntimeCoverageWeight,
            MidpointRounding.AwayFromZero);
        if (coverageDeduction > 0)
        {
            deductions.Add(new Deduction("RUN-COVERAGE-LOW", coverageDeduction,
                $"Scan coverage {coverageRate:P0} below full.", Array.Empty<string>()));
            codes.Add("RUN-COVERAGE-LOW");
        }

        var resolutionRate = Math.Clamp(a.ScanCoverage.DependencyResolutionRate, 0, 1);
        var resolutionDeduction = (int)Math.Round((1 - resolutionRate) * ScoringRuleset.RuntimeResolutionWeight,
            MidpointRounding.AwayFromZero);
        if (resolutionDeduction > 0)
        {
            deductions.Add(new Deduction("RUN-DEP-RESOLUTION-LOW", resolutionDeduction,
                $"Dependency resolution rate {resolutionRate:P0} below full.", Array.Empty<string>()));
            codes.Add("RUN-DEP-RESOLUTION-LOW");
        }

        if (a.ScanCoverage.ScannersFailed.Count > 0)
        {
            var scannerDeduction = Math.Min(
                ScoringRuleset.RuntimeScannerFailedPerScanner * a.ScanCoverage.ScannersFailed.Count,
                ScoringRuleset.RuntimeScannerFailedMax);
            deductions.Add(new Deduction("RUN-SCANNER-FAILED", scannerDeduction,
                $"{a.ScanCoverage.ScannersFailed.Count} scanner(s) failed.", Array.Empty<string>()));
            codes.Add("RUN-SCANNER-FAILED");
        }

        var rawScore = Math.Clamp(100 - deductions.Sum(d => d.Magnitude), 0, 100);

        var confidence = Math.Min(coverageRate, resolutionRate);
        if (a.ScanCoverage.ScannersFailed.Count > 0)
        {
            confidence *= 0.5;
        }
        confidence = Math.Clamp(confidence, 0, 1);

        return new DimensionScore(
            DimensionKey: DimensionKey.RuntimeAndValidationEvidence,
            WeightPct: ScoringRuleset.WeightRuntime,
            RawScore: rawScore,
            WeightedContribution: Math.Round(rawScore * ScoringRuleset.WeightRuntime / 100.0, 2),
            Deductions: deductions,
            Confidence: Math.Round(confidence, 2),
            RationaleCodes: codes.ToArray(),
            EvidenceIds: evidenceIds.ToArray());
    }

    // ---- Dimension 5: Windows experience ----

    private static DimensionScore ScoreWindows(RepositoryAssessmentV1 a)
    {
        var deductions = new List<Deduction>();
        var codes = new SortedSet<string>(StringComparer.Ordinal);
        // WindowsExperience carries no top-level evidenceId; nested Evidence
        // records have file paths, not IDs, so no citable IDs at this dimension.
        var evidenceIds = Array.Empty<string>();

        var w = a.WindowsExperience;
        var uiDeduction = ScoringRuleset.UiTechnologyDeduction(w.UiTechnology);
        if (uiDeduction > 0)
        {
            var (code, desc) = w.UiTechnology switch
            {
                UiTechnology.Wpf or UiTechnology.WinForms or UiTechnology.Uwp
                    or UiTechnology.Maui or UiTechnology.XamlIslands =>
                    ("WIN-UI-LEGACY", $"UI technology '{EnumMemberValue(w.UiTechnology)}' predates WinUI 3."),
                UiTechnology.Electron or UiTechnology.Qt or UiTechnology.Tauri
                    or UiTechnology.Flutter or UiTechnology.Gtk =>
                    ("WIN-UI-CROSS-PLATFORM", $"UI technology '{EnumMemberValue(w.UiTechnology)}' is cross-platform."),
                _ => ("WIN-UI-UNKNOWN-OR-ABSENT", $"UI technology '{EnumMemberValue(w.UiTechnology)}' is unknown or absent."),
            };
            deductions.Add(new Deduction(code, uiDeduction, desc, Array.Empty<string>()));
            codes.Add(code);
        }

        if (!w.InstallerExists)
        {
            deductions.Add(new Deduction("WIN-NO-INSTALLER", ScoringRuleset.WinNoInstaller,
                "No Windows installer detected.", Array.Empty<string>()));
            codes.Add("WIN-NO-INSTALLER");
        }
        if (!w.OfflineCapable)
        {
            deductions.Add(new Deduction("WIN-NO-OFFLINE", ScoringRuleset.WinNoOffline,
                "App is not offline capable.", Array.Empty<string>()));
            codes.Add("WIN-NO-OFFLINE");
        }

        switch (w.AccessibilityEvidence)
        {
            case AccessibilityEvidenceLevel.None:
                deductions.Add(new Deduction("WIN-A11Y-NONE", ScoringRuleset.WinA11yNone,
                    "No accessibility evidence.", Array.Empty<string>()));
                codes.Add("WIN-A11Y-NONE");
                break;
            case AccessibilityEvidenceLevel.Partial:
                deductions.Add(new Deduction("WIN-A11Y-PARTIAL", ScoringRuleset.WinA11yPartial,
                    "Only partial accessibility evidence.", Array.Empty<string>()));
                codes.Add("WIN-A11Y-PARTIAL");
                break;
            case AccessibilityEvidenceLevel.Unknown:
                deductions.Add(new Deduction("WIN-A11Y-UNKNOWN", ScoringRuleset.WinA11yUnknown,
                    "Accessibility evidence not observed.", Array.Empty<string>()));
                codes.Add("WIN-A11Y-UNKNOWN");
                break;
        }

        if (!w.NotificationsIntegrated)
        {
            deductions.Add(new Deduction("WIN-NO-NOTIFICATIONS", ScoringRuleset.WinNoNotifications,
                "Windows notifications integration not detected.", Array.Empty<string>()));
            codes.Add("WIN-NO-NOTIFICATIONS");
        }
        if (!w.LifecycleIntegrated)
        {
            deductions.Add(new Deduction("WIN-NO-LIFECYCLE", ScoringRuleset.WinNoLifecycle,
                "Windows lifecycle integration not detected.", Array.Empty<string>()));
            codes.Add("WIN-NO-LIFECYCLE");
        }

        var rawScore = Math.Clamp(100 - deductions.Sum(d => d.Magnitude), 0, 100);

        var confidence = w.UiTechnology == UiTechnology.Unknown ? 0.40 : 0.80;
        if (a.Unknowns.Any(u => u.Area == UnknownArea.WindowsExperience))
        {
            confidence = Math.Min(confidence, 0.60);
        }

        return new DimensionScore(
            DimensionKey: DimensionKey.WindowsExperienceAndDeployment,
            WeightPct: ScoringRuleset.WeightWindows,
            RawScore: rawScore,
            WeightedContribution: Math.Round(rawScore * ScoringRuleset.WeightWindows / 100.0, 2),
            Deductions: deductions,
            Confidence: Math.Round(confidence, 2),
            RationaleCodes: codes.ToArray(),
            EvidenceIds: evidenceIds.ToArray());
    }

    // ---- Caps ----

    private static IReadOnlyList<CapApplication> BuildCaps(DependencyResult dep, BuildResult build)
    {
        var caps = new List<CapApplication>();

        if (dep.DriverBlockers.Count > 0)
        {
            caps.Add(new CapApplication(
                CapId: CapId.RequiredUnsupportedDriverLe30,
                Ceiling: ScoringRuleset.CapUnsupportedDriverCeiling,
                Description: "One or more required drivers are blocked on ARM64.",
                TriggeredBy: dep.DriverBlockers
                    .Select(d => d.EvidenceId)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(id => id, StringComparer.Ordinal)
                    .ToArray()));
        }

        if (dep.NativeBlockersNoRepl.Count > 0)
        {
            caps.Add(new CapApplication(
                CapId: CapId.RequiredX64OnlyNativeLe40,
                Ceiling: ScoringRuleset.CapX64OnlyNativeCeiling,
                Description: "One or more required native/COM dependencies are x64-only without replacement.",
                TriggeredBy: dep.NativeBlockersNoRepl
                    .Select(d => d.EvidenceId)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(id => id, StringComparer.Ordinal)
                    .ToArray()));
        }

        if (build.NoArm64Target)
        {
            caps.Add(new CapApplication(
                CapId: CapId.NoArm64OrArm64EcTargetLe60,
                Ceiling: ScoringRuleset.CapNoArm64TargetCeiling,
                Description: "No ARM64 or Arm64EC build target detected.",
                TriggeredBy: new[] { build.Dimension.EvidenceIds[0] }));
        }

        return caps;
    }

    // ---- Major blockers ----

    private static IReadOnlyList<MajorBlocker> BuildMajorBlockers(
        RepositoryAssessmentV1 a,
        DependencyResult dep,
        BuildResult build,
        IReadOnlyList<CapApplication> caps,
        CodeResult code)
    {
        var blockers = new List<MajorBlocker>();

        foreach (var d in dep.DriverBlockers)
        {
            blockers.Add(new MajorBlocker(
                BlockerId: $"bl-drv-{SlugifyBlockerName(d.Name)}",
                Category: BlockerCategory.Dependency,
                Description: $"Required driver '{d.Name}' has no ARM64 build.",
                EvidenceIds: new[] { d.EvidenceId }));
        }

        foreach (var d in dep.NativeBlockersNoRepl)
        {
            blockers.Add(new MajorBlocker(
                BlockerId: $"bl-native-{SlugifyBlockerName(d.Name)}",
                Category: BlockerCategory.Dependency,
                Description: $"Required native dependency '{d.Name}' is x64-only with no replacement.",
                EvidenceIds: new[] { d.EvidenceId }));
        }

        if (build.NoArm64Target)
        {
            blockers.Add(new MajorBlocker(
                BlockerId: "bl-no-arm64-target",
                Category: BlockerCategory.Build,
                Description: "Build does not produce an ARM64 or Arm64EC target.",
                EvidenceIds: new[] { a.BuildFindings.EvidenceId }));
        }

        foreach (var f in code.CriticalFindings)
        {
            blockers.Add(new MajorBlocker(
                BlockerId: $"bl-code-crit-{SlugifyBlockerName(f.RuleId)}",
                Category: BlockerCategory.Code,
                Description: $"Critical code finding '{f.RuleId}': {f.Description}",
                EvidenceIds: new[] { f.EvidenceId }));
        }

        // Deduplicate by BlockerId (sorted for determinism).
        return blockers
            .GroupBy(b => b.BlockerId, StringComparer.Ordinal)
            .Select(g => g.First())
            .OrderBy(b => b.BlockerId, StringComparer.Ordinal)
            .ToArray();
    }

    // ---- Provisional state ----

    private static (bool Provisional, IReadOnlyList<ProvisionalReason> Reasons) ProvisionalState(
        RepositoryAssessmentV1 a)
    {
        var reasons = new List<ProvisionalReason>();

        if (a.ScanCoverage.FilesTotal > 0)
        {
            var rate = a.ScanCoverage.FilesScanned / (double)a.ScanCoverage.FilesTotal;
            if (rate < ScoringRuleset.ScanCoverageMin)
            {
                reasons.Add(ProvisionalReason.ScanCoverageLow);
            }
        }

        if (a.ScanCoverage.DependencyResolutionRate < ScoringRuleset.DependencyResolutionMin)
        {
            reasons.Add(ProvisionalReason.DependencyResolutionLow);
        }

        if (a.ScanCoverage.ScannersFailed.Count > 0)
        {
            reasons.Add(ProvisionalReason.ScannerFailed);
        }

        var dimensionMissing = a.Unknowns.Any(u =>
            u.Area == UnknownArea.Dependency
            || u.Area == UnknownArea.Code
            || u.Area == UnknownArea.Build
            || u.Area == UnknownArea.WindowsExperience);
        if (dimensionMissing)
        {
            reasons.Add(ProvisionalReason.DimensionMissingEvidence);
        }

        if (a.ScanCoverage.FilesTotal == 0)
        {
            reasons.Add(ProvisionalReason.InsufficientSignals);
        }

        return (reasons.Count > 0, reasons);
    }

    // ---- Banding ----

    private static ReadinessBand ClassifyBand(RepositoryAssessmentV1 a, int overall, bool provisional)
    {
        if (provisional)
        {
            var total = Math.Max(1L, a.ScanCoverage.FilesTotal);
            var coverageRate = a.ScanCoverage.FilesScanned / (double)total;
            if (a.ScanCoverage.FilesTotal == 0 || coverageRate < ScoringRuleset.InsufficientEvidenceCoverageMax)
            {
                return ReadinessBand.InsufficientEvidence;
            }
        }

        return overall switch
        {
            >= ScoringRuleset.BandReadyMin => ReadinessBand.ReadyOrMinorChanges,
            >= ScoringRuleset.BandModerateMin => ReadinessBand.ModerateMigration,
            >= ScoringRuleset.BandRemediationMin => ReadinessBand.SignificantRemediation,
            _ => ReadinessBand.BlockedOrMajorRedesign,
        };
    }

    // ---- Helpers ----

    private static string SlugifyRuleId(string ruleId)
    {
        var chars = (ruleId ?? string.Empty).ToUpperInvariant().ToCharArray();
        var buf = new System.Text.StringBuilder(chars.Length);
        var lastDash = true;
        foreach (var c in chars)
        {
            if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'))
            {
                buf.Append(c);
                lastDash = false;
            }
            else if (!lastDash && buf.Length > 0)
            {
                buf.Append('-');
                lastDash = true;
            }
        }
        var slug = buf.ToString().Trim('-');
        if (string.IsNullOrEmpty(slug))
        {
            return "UNKNOWN";
        }
        // Schema: max 10 segments after the leading uppercase word; we consume 3 (CODE-<SEVERITY>-<rule>).
        var segments = slug.Split('-');
        if (segments.Length > 8)
        {
            segments = segments.Take(8).ToArray();
        }
        return string.Join('-', segments);
    }

    private static string SlugifyBlockerName(string name)
    {
        var chars = (name ?? string.Empty).ToLowerInvariant().ToCharArray();
        var buf = new System.Text.StringBuilder(chars.Length);
        var lastDash = true;
        foreach (var c in chars)
        {
            if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
            {
                buf.Append(c);
                lastDash = false;
            }
            else if (!lastDash && buf.Length > 0)
            {
                buf.Append('-');
                lastDash = true;
            }
        }
        var slug = buf.ToString().Trim('-');
        if (string.IsNullOrEmpty(slug))
        {
            slug = "unknown";
        }
        // bl- prefix consumes 3 chars; remaining slug capped at 77 to fit 80-char limit on `[a-z0-9-]`.
        if (slug.Length > 77)
        {
            slug = slug[..77].TrimEnd('-');
        }
        return slug;
    }

    private static string EnumMemberValue<TEnum>(TEnum value) where TEnum : struct, Enum
    {
        var name = value.ToString();
        var member = typeof(TEnum).GetField(name, BindingFlags.Public | BindingFlags.Static);
        var attr = member?.GetCustomAttribute<EnumMemberAttribute>();
        return attr?.Value ?? name;
    }
}
