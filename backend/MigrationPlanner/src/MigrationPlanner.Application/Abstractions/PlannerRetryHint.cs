namespace MigrationPlanner.Application.Abstractions;

/// <summary>
/// Structured correction hint the orchestrator hands the model on retry when
/// the first attempt violated the deterministic recommendation dispatch. The
/// model must reproduce its previous plan, changing only <c>recommendedPath</c>
/// (and, if it disagrees, <c>confidence</c>) plus any dependent fields such as
/// <c>alternatives[]</c> that must stay internally consistent.
/// </summary>
public sealed record PlannerRetryHint(
    string PreviousRecommendedPath,
    string PreviousConfidence,
    string ExpectedRecommendedPath,
    string ExpectedConfidence,
    string Diagnostic);
