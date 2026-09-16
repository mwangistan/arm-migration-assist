using MigrationPlanner.Domain.Assessment;

namespace MigrationPlanner.Infrastructure.Scoring;

/// <summary>
/// Version-stamped scoring constants. See
/// <c>backend/MigrationPlanner/docs/decisions/0001-deterministic-scoring.md</c>
/// for the authoritative rationale. Any change here MUST bump <see cref="RulesetId"/>.
/// </summary>
internal static class ScoringRuleset
{
    public const string RulesetId = "scoring-v1";
    public const string ScorerName = "arm-migration-assist-scorer";
    public const string ScorerVersion = "0.1.0";

    // Weights (must sum to 100).
    public const int WeightDependency = 30;
    public const int WeightCode = 25;
    public const int WeightBuild = 20;
    public const int WeightRuntime = 15;
    public const int WeightWindows = 10;

    // Cap ceilings.
    public const int CapUnsupportedDriverCeiling = 30;
    public const int CapX64OnlyNativeCeiling = 40;
    public const int CapNoArm64TargetCeiling = 60;

    // Bands.
    public const int BandReadyMin = 85;
    public const int BandModerateMin = 70;
    public const int BandRemediationMin = 50;

    // Confidence banding.
    public const double ConfidenceHighMin = 0.75;
    public const double ConfidenceMediumMin = 0.50;

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

    // Code deductions per severity.
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
}
