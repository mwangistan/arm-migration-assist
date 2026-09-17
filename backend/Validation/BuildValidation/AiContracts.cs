namespace Validation.BuildValidation;

public sealed record ValidationPlannerRequest(
    RepositoryContext Repository, MigrationPlan MigrationPlan, PreparedValidation DeterministicProposal);

// Additional commands are unclassified custom checks. AI cannot upgrade them to measured ARM64 coverage.
public sealed record SuggestedCommand(
    string Id, string Description, string Executable, IReadOnlyList<string> Arguments,
    string WorkingDirectory, IReadOnlyDictionary<string, string> Environment,
    int TimeoutSeconds, IReadOnlyList<string> DependsOn, IReadOnlyList<string> CriterionKeys);
public sealed record ValidationPlannerResponse(
    IReadOnlyList<CommandMapping> Mappings,
    IReadOnlyList<SuggestedCommand> AdditionalCommands,
    IReadOnlyList<ManualCheck> ManualChecks,
    string Rationale);
public sealed record EvidenceAnalysisRequest(
    RepositoryContext Repository, IReadOnlyList<ValidationCommand> ApprovedPlanCommands,
    IReadOnlyList<CommandResult> Results, IReadOnlyList<ExecutionEvidence> Evidence);
public sealed record CoverageReviewRequest(
    IReadOnlyList<string> TargetDevices, Scorecard Scorecard,
    IReadOnlyList<CoverageGap> DeterministicGaps, IReadOnlyList<ExecutionEvidence> Evidence);

public interface IValidationPlanner
{
    Task<ValidationPlannerResponse> PlanAsync(ValidationPlannerRequest request, CancellationToken cancellationToken);
}
public interface IEvidenceAnalyzer
{
    Task<EvidenceAnalysisResponse> AnalyzeAsync(EvidenceAnalysisRequest request, CancellationToken cancellationToken);
}
public interface ICoverageReviewer
{
    Task<CoverageReviewResponse> ReviewAsync(CoverageReviewRequest request, CancellationToken cancellationToken);
}

// Future CI adapters observe an existing run; Feature 4 never provisions or starts a pipeline.
// Observations are intentionally not accepted by the local executor as trusted command results.
public sealed record CiObservationRequest(string RepositoryUrl, string CommitSha, string RunReference);
public sealed record CiObservation(
    string CommitSha, string RunReference, string State, IReadOnlyList<string> ArtifactReferences);
public interface ICiValidationObserver
{
    Task<CiObservation> ObserveAsync(CiObservationRequest request, CancellationToken cancellationToken);
}
