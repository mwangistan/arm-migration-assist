using MigrationPlanner.Domain.Plan;

namespace MigrationPlanner.Domain.Plan;

/// <summary>
/// Deterministic mapping from <see cref="ReadinessScoreV1"/> to
/// <c>(recommendedPath, confidence)</c>. This is the single source of truth
/// for step 5 of the planner prompt and is enforced server-side by the plan
/// safety validator.
/// </summary>
public static class RecommendationDispatch
{
    public const string PathNativeArm64 = "native-arm64";
    public const string PathArm64Ec = "arm64ec";
    public const string PathStaged = "staged";
    public const string PathWinUi3 = "winui3";
    public const string PathInsufficientEvidence = "insufficient-evidence";

    public const string ConfidenceHigh = "high";
    public const string ConfidenceMedium = "medium";
    public const string ConfidenceLow = "low";

    public static (string Path, string Confidence) Choose(ReadinessScoreV1 score)
    {
        ArgumentNullException.ThrowIfNull(score);

        var path = ChoosePath(score);
        var confidence = ChooseConfidence(score, path);
        return (path, confidence);
    }

    private static string ChoosePath(ReadinessScoreV1 score)
    {
        if (score.Band == ReadinessBand.InsufficientEvidence)
        {
            return PathInsufficientEvidence;
        }

        foreach (var cap in score.CapsApplied)
        {
            switch (cap.CapId)
            {
                case CapId.RequiredUnsupportedDriverLe30:
                    return PathStaged;
                case CapId.RequiredX64OnlyNativeLe40:
                    return PathArm64Ec;
                case CapId.NoArm64OrArm64EcTargetLe60:
                    return PathNativeArm64;
            }
        }

        return score.Band switch
        {
            ReadinessBand.ReadyOrMinorChanges => PathNativeArm64,
            ReadinessBand.ModerateMigration => PathNativeArm64,
            ReadinessBand.SignificantRemediation => PathStaged,
            ReadinessBand.BlockedOrMajorRedesign => PathArm64Ec,
            _ => PathInsufficientEvidence,
        };
    }

    private static string ChooseConfidence(ReadinessScoreV1 score, string path)
    {
        if (path == PathInsufficientEvidence)
        {
            return ConfidenceLow;
        }
        if (score.Provisional)
        {
            return ConfidenceLow;
        }
        return score.Band switch
        {
            ReadinessBand.ReadyOrMinorChanges => ConfidenceHigh,
            ReadinessBand.ModerateMigration => ConfidenceMedium,
            _ => ConfidenceLow,
        };
    }
}
