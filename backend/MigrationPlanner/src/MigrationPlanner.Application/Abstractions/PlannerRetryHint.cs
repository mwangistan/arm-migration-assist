namespace MigrationPlanner.Application.Abstractions;

/// <summary>
/// Reason the orchestrator is re-invoking the model. Determines which
/// correction section the prompt builder emits.
/// </summary>
public enum PlannerRetryReason
{
    RecommendationInconsistent,
    SkillMissing,
    SkillIoMismatch,
    ShapeInvalid,
    UnderGranular,
    MissingEvidence,
}

public sealed record PlannerSkillIoContract(
    string Name,
    string Description,
    bool WriteAccess,
    IReadOnlyList<string> SupportedInputs,
    IReadOnlyList<string> SupportedOutputs);

/// <summary>
/// Structured correction hint the orchestrator hands the model on retry when
/// the first attempt failed a server-side check. Kind-specific fields are
/// populated per <see cref="Reason"/>. <see cref="PreviousPlanJson"/> is the
/// raw JSON of the rejected first attempt so the model can echo unchanged
/// fields byte-for-byte and only patch what the correction requires.
/// </summary>
public sealed record PlannerRetryHint(
    PlannerRetryReason Reason,
    string Diagnostic,
    string PreviousPlanJson,
    string? PreviousRecommendedPath = null,
    string? PreviousConfidence = null,
    string? ExpectedRecommendedPath = null,
    string? ExpectedConfidence = null,
    IReadOnlyList<string>? UnresolvedSkills = null,
    IReadOnlyList<string>? MissingBuckets = null,
    IReadOnlyList<string>? InvalidEvidenceIds = null,
    IReadOnlyList<string>? AllowedEvidenceIds = null,
    IReadOnlyList<PlannerSkillIoContract>? AvailableSkillContracts = null)
{
    public static PlannerRetryHint ForRecommendation(
        string previousPath, string previousConfidence,
        string expectedPath, string expectedConfidence,
        string diagnostic, string previousPlanJson) =>
        new(
            Reason: PlannerRetryReason.RecommendationInconsistent,
            Diagnostic: diagnostic,
            PreviousPlanJson: previousPlanJson,
            PreviousRecommendedPath: previousPath,
            PreviousConfidence: previousConfidence,
            ExpectedRecommendedPath: expectedPath,
            ExpectedConfidence: expectedConfidence);

    public static PlannerRetryHint ForMissingSkill(
        IReadOnlyList<string> unresolvedSkills, string diagnostic, string previousPlanJson) =>
        new(
            Reason: PlannerRetryReason.SkillMissing,
            Diagnostic: diagnostic,
            PreviousPlanJson: previousPlanJson,
            UnresolvedSkills: unresolvedSkills);

    public static PlannerRetryHint ForSkillIoMismatch(
        IReadOnlyList<PlannerSkillIoContract> availableSkillContracts,
        string diagnostic,
        string previousPlanJson) =>
        new(
            Reason: PlannerRetryReason.SkillIoMismatch,
            Diagnostic: diagnostic,
            PreviousPlanJson: previousPlanJson,
            AvailableSkillContracts: availableSkillContracts);

    public static PlannerRetryHint ForShapeInvalid(string diagnostic, string previousPlanJson) =>
        new(
            Reason: PlannerRetryReason.ShapeInvalid,
            Diagnostic: diagnostic,
            PreviousPlanJson: previousPlanJson);

    public static PlannerRetryHint ForUnderGranular(
        string diagnostic, string previousPlanJson, IReadOnlyList<string> missingBuckets) =>
        new(
            Reason: PlannerRetryReason.UnderGranular,
            Diagnostic: diagnostic,
            PreviousPlanJson: previousPlanJson,
            MissingBuckets: missingBuckets);

    public static PlannerRetryHint ForMissingEvidence(
        IReadOnlyList<string> invalidEvidenceIds,
        IReadOnlyList<string> allowedEvidenceIds,
        string diagnostic,
        string previousPlanJson) =>
        new(
            Reason: PlannerRetryReason.MissingEvidence,
            Diagnostic: diagnostic,
            PreviousPlanJson: previousPlanJson,
            InvalidEvidenceIds: invalidEvidenceIds,
            AllowedEvidenceIds: allowedEvidenceIds);
}
