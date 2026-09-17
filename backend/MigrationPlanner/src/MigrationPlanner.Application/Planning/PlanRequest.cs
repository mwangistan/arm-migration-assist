using MigrationPlanner.Domain.Assessment;

namespace MigrationPlanner.Application.Planning;

public sealed record PlanRequest(RepositoryAssessmentV1 Assessment, string? RunId = null);
