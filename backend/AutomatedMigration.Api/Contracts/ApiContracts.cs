using System.Text.Json.Serialization;
using AutomatedMigration.Models;

namespace AutomatedMigration.Api.Contracts;

// Wire contracts for POST /api/migration-actions. Kept independent of
// AutomatedMigration.Models.MigrationPlan (which the runner already accepts) to
// let the API contract evolve without breaking the CLI.

public sealed record MigrationActionsRequest(
    [property: JsonPropertyName("plan")] MigrationPlan Plan,
    [property: JsonPropertyName("target")] RepositoryTarget Target);

public sealed record RepositoryTarget(
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("commitSha")] string CommitSha);

public sealed record MigrationActionsAccepted(
    [property: JsonPropertyName("jobId")] string JobId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("statusUrl")] string StatusUrl);

public sealed record MigrationActionsJobStatus(
    [property: JsonPropertyName("jobId")] string JobId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("planId")] string PlanId,
    [property: JsonPropertyName("target")] RepositoryTarget Target,
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("startedAt")] DateTimeOffset? StartedAt,
    [property: JsonPropertyName("finishedAt")] DateTimeOffset? FinishedAt,
    [property: JsonPropertyName("result")] MigrationActionsResult? Result,
    [property: JsonPropertyName("error")] string? Error);

public sealed record MigrationActionsResult(
    [property: JsonPropertyName("planId")] string PlanId,
    [property: JsonPropertyName("sourceCommitSha")] string SourceCommitSha,
    [property: JsonPropertyName("generated")] IReadOnlyList<GeneratedPatchDto> Generated,
    [property: JsonPropertyName("skipped")] IReadOnlyList<SkippedWorkItemDto> Skipped);

public sealed record GeneratedPatchDto(
    [property: JsonPropertyName("workItemId")] string WorkItemId,
    [property: JsonPropertyName("agentOrSkill")] string AgentOrSkill,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("diff")] string Diff,
    [property: JsonPropertyName("originalSizeBytes")] int OriginalSizeBytes,
    [property: JsonPropertyName("truncated")] bool Truncated,
    [property: JsonPropertyName("evidenceIds")] IReadOnlyList<string> EvidenceIds,
    [property: JsonPropertyName("acceptanceTests")] IReadOnlyList<AcceptanceTest> AcceptanceTests);

public sealed record SkippedWorkItemDto(
    [property: JsonPropertyName("workItemId")] string WorkItemId,
    [property: JsonPropertyName("agentOrSkill")] string AgentOrSkill,
    [property: JsonPropertyName("reason")] string Reason);
