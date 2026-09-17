using MigrationPlanner.Domain.Assessment;

namespace MigrationPlanner.Infrastructure.Scoring;

/// <summary>
/// Version-stamped scoring constants. See
/// <c>backend/MigrationPlanner/docs/decisions/0002-scoring-v2.md</c>
/// for the authoritative rationale. Any change here MUST bump <see cref="RulesetId"/>.
/// </summary>
internal static class ScoringRuleset
{
    public const string RulesetId = "scoring-v2";
    public const string ScorerName = "arm-migration-assist-scorer";
    public const string ScorerVersion = "0.2.0";

    // Base weights (Windows-scoped, sum = 100).
    public const int WeightDependency = 30;
    public const int WeightCode = 25;
    public const int WeightBuild = 20;
    public const int WeightRuntime = 15;
    public const int WeightWindows = 10;

    // Skip-Windows weights (Windows dim contributes 0, its 10% redistributed; sum = 100).
    public const int WeightDependencyNoWin = 33;
    public const int WeightCodeNoWin = 28;
    public const int WeightBuildNoWin = 22;
    public const int WeightRuntimeNoWin = 17;
    public const int WeightWindowsNoWin = 0;

    // Cap ceilings.
    public const int CapUnsupportedDriverCeiling = 30;
    public const int CapX64OnlyNativeCeiling = 40;
    public const int CapNoArm64TargetCeiling = 60;

    // Bands.
    public const int BandReadyMin = 85;
    public const int BandModerateMin = 70;
    public const int BandRemediationMin = 50;

    // Evidence-completeness banding.
    public const double CompletenessHighMin = 0.75;
    public const double CompletenessMediumMin = 0.50;

    // Provisional thresholds.
    public const double ScanCoverageMin = 0.60;
    public const double DependencyResolutionMin = 0.60;
    public const double InsufficientEvidenceCoverageMax = 0.30;

    // Dependency deductions.
    public const int DepRequiredBlockedNoRepl = 30;
    public const int DepRequiredBlockedWithRepl = 20;
    public const int DepRequiredEmulation = 12;
    public const int DepRequiredUnknown = 8;
    public const int DepOptionalBlockedNoRepl = 6;
    public const int DepOptionalBlockedWithRepl = 4;
    public const int DepOptionalEmulation = 3;
    public const int DepOptionalUnknown = 2;

    public static int CodeSeverityWeight(Severity severity) => severity switch
    {
        Severity.Critical => 25,
        Severity.High => 12,
        Severity.Medium => 5,
        Severity.Low => 1,
        Severity.Informational => 0,
        _ => 0,
    };

    // Build deductions.
    public const int BuildNoArm64Target = 50;
    public const int BuildNoArm64Ci = 20;
    public const int BuildNoPackaging = 15;
    public const int BuildNoTests = 15;

    // Runtime deductions.
    public const int RuntimeNoTests = 30;
    public const int RuntimeCoverageWeight = 40;
    public const int RuntimeResolutionWeight = 40;
    public const int RuntimeScannerFailedPerScanner = 10;
    public const int RuntimeScannerFailedMax = 40;

    // Windows deductions.
    public const int WinNoInstaller = 15;
    public const int WinNoOffline = 8;
    public const int WinA11yNone = 15;
    public const int WinA11yPartial = 8;
    public const int WinA11yUnknown = 10;
    public const int WinNoNotifications = 5;
    public const int WinNoLifecycle = 5;

    public static int UiTechnologyDeduction(UiTechnology ui) => ui switch
    {
        UiTechnology.WinUi3 => 0,
        UiTechnology.Wpf or UiTechnology.WinForms or UiTechnology.Uwp
            or UiTechnology.Maui or UiTechnology.XamlIslands => 10,
        UiTechnology.Electron or UiTechnology.Qt or UiTechnology.Tauri
            or UiTechnology.Flutter or UiTechnology.Gtk => 25,
        _ => 35,
    };

    // --- Signal-scoped weight selection ---

    private static readonly HashSet<string> InterpretedLanguages = new(StringComparer.OrdinalIgnoreCase)
    {
        "python", "javascript", "typescript", "ruby", "php", "perl", "lua",
    };

    private static readonly HashSet<string> WindowsUiFrameworks = new(StringComparer.OrdinalIgnoreCase)
    {
        "winui3", "winui", "wpf", "winforms", "uwp", "maui",
    };

    public static bool IsInterpretedOnly(RepositoryAssessmentV1 assessment)
    {
        var langs = assessment.Technology.Languages;
        if (langs.Count == 0)
        {
            return false;
        }
        foreach (var lang in langs)
        {
            if (!InterpretedLanguages.Contains(lang))
            {
                return false;
            }
        }
        return true;
    }

    public static bool IsNonWindowsApp(RepositoryAssessmentV1 assessment)
    {
        var w = assessment.WindowsExperience;
        switch (w.UiTechnology)
        {
            case UiTechnology.Web:
            case UiTechnology.Cli:
            case UiTechnology.None:
                return true;
            case UiTechnology.Unknown:
                var hasPositive = w.WindowsVersionExists
                    || w.InstallerExists
                    || w.OfflineCapable
                    || w.NotificationsIntegrated
                    || w.LifecycleIntegrated
                    || w.AccessibilityEvidence == AccessibilityEvidenceLevel.Full
                    || w.AccessibilityEvidence == AccessibilityEvidenceLevel.Partial;
                var hasWindowsFramework = false;
                foreach (var f in assessment.Technology.Frameworks)
                {
                    if (WindowsUiFrameworks.Contains(f))
                    {
                        hasWindowsFramework = true;
                        break;
                    }
                }
                return !hasPositive && !hasWindowsFramework;
            default:
                return false;
        }
    }

    // --- Rationale-code descriptions ---

    private static readonly Dictionary<string, string> BaseDescriptions = new(StringComparer.Ordinal)
    {
        ["DEP-BLOCKED-REQUIRED-NO-REPL"] = "A required dependency has no ARM64 build and no replacement candidate.",
        ["DEP-BLOCKED-REQUIRED-WITH-REPL"] = "A required dependency has no ARM64 build; replacement candidates exist.",
        ["DEP-EMULATION-REQUIRED"] = "One or more required dependencies run only under emulation on ARM64.",
        ["DEP-UNKNOWN-REQUIRED"] = "The ARM64 status of a required dependency could not be determined.",
        ["DEP-BLOCKED-OPTIONAL"] = "An optional dependency has no ARM64 build.",
        ["DEP-BLOCKED-OPTIONAL-WITH-REPL"] = "An optional dependency has no ARM64 build; replacement candidates exist.",
        ["DEP-EMULATION-OPTIONAL"] = "An optional dependency runs only under emulation on ARM64.",
        ["DEP-UNKNOWN-OPTIONAL"] = "The ARM64 status of an optional dependency could not be determined.",
        ["BUILD-NO-ARM64"] = "Neither an ARM64 nor an Arm64EC build target was detected.",
        ["BUILD-NO-ARM64-CI"] = "No ARM64 CI job was detected.",
        ["BUILD-PKG-NO-ARM64"] = "Packaging does not declare ARM64 support.",
        ["BUILD-NO-TESTS"] = "No test suite was detected in the repository.",
        ["BUILD-CAP-SUPPRESSED-INTERPRETED"] = "The no-arm64-target cap was suppressed because this is an interpreted-language project.",
        ["RUN-NO-TESTS"] = "Assessment reports no tests exist.",
        ["RUN-COVERAGE-LOW"] = "File-level scan coverage is below full.",
        ["RUN-DEP-RESOLUTION-LOW"] = "Dependency resolution rate is below full.",
        ["RUN-SCANNER-FAILED"] = "One or more scanners failed to complete.",
        ["WIN-UI-LEGACY"] = "UI technology predates WinUI 3.",
        ["WIN-UI-CROSS-PLATFORM"] = "UI technology is a cross-platform toolkit.",
        ["WIN-UI-UNKNOWN-OR-ABSENT"] = "UI technology is unknown or absent.",
        ["WIN-NO-INSTALLER"] = "No Windows installer was detected.",
        ["WIN-NO-OFFLINE"] = "The application is not offline-capable.",
        ["WIN-A11Y-NONE"] = "No accessibility evidence was observed.",
        ["WIN-A11Y-PARTIAL"] = "Only partial accessibility evidence was observed.",
        ["WIN-A11Y-UNKNOWN"] = "Accessibility evidence could not be determined.",
        ["WIN-NO-NOTIFICATIONS"] = "Windows notifications integration was not detected.",
        ["WIN-NO-LIFECYCLE"] = "Windows lifecycle integration was not detected.",
        ["WIN-DIM-SKIPPED-NOT-WINDOWS-APP"] = "Windows-experience dimension was skipped because no Windows-native surface was detected.",
        ["CAP-REQUIRED-UNSUPPORTED-DRIVER-LE-30"] = "Required driver has no ARM64 build; overall score capped at 30.",
        ["CAP-REQUIRED-X64-ONLY-NATIVE-LE-40"] = "Required native/COM dependency is x64-only with no replacement; overall score capped at 40.",
        ["CAP-NO-ARM64-OR-ARM64EC-TARGET-LE-60"] = "No ARM64 or Arm64EC build target detected; overall score capped at 60.",
    };

    public static string DescribeRationale(string code)
    {
        if (BaseDescriptions.TryGetValue(code, out var desc))
        {
            return desc;
        }
        // Prefix-based fallback for dynamic codes like CODE-CRITICAL-<ruleId>.
        if (code.StartsWith("CODE-CRITICAL-", StringComparison.Ordinal))
        {
            return "A critical code finding was detected.";
        }
        if (code.StartsWith("CODE-HIGH-", StringComparison.Ordinal))
        {
            return "A high-severity code finding was detected.";
        }
        if (code.StartsWith("CODE-MEDIUM-", StringComparison.Ordinal))
        {
            return "A medium-severity code finding was detected.";
        }
        if (code.StartsWith("CODE-LOW-", StringComparison.Ordinal))
        {
            return "A low-severity code finding was detected.";
        }
        return code;
    }
}

