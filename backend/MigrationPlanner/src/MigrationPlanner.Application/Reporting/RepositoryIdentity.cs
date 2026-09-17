namespace MigrationPlanner.Application.Reporting;

public sealed record RepositoryIdentity(
    string Name,
    string Url,
    string CommitSha,
    string DefaultBranch,
    string? License);
