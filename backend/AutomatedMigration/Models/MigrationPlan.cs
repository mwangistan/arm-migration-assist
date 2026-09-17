using System.Text.Json.Serialization;

namespace AutomatedMigration.Models;

// The subset of MigrationPlanV1 (the Planner's output) that Feature 3 reads.
// The full schema has many more fields; we only bind what the generators need.
public sealed record MigrationPlan(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("planId")] string PlanId,
    [property: JsonPropertyName("recommendedPath")] string RecommendedPath,
    [property: JsonPropertyName("workItems")] IReadOnlyList<WorkItem> WorkItems);

public sealed record WorkItem(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("sequence")] int Sequence,
    [property: JsonPropertyName("priority")] string Priority,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("objective")] string Objective,
    [property: JsonPropertyName("agentOrSkill")] string AgentOrSkill,
    [property: JsonPropertyName("inputs")] IReadOnlyList<string>? Inputs,
    [property: JsonPropertyName("expectedOutputs")] IReadOnlyList<string>? ExpectedOutputs,
    [property: JsonPropertyName("dependencies")] IReadOnlyList<string>? Dependencies,
    [property: JsonPropertyName("evidenceIds")] IReadOnlyList<string>? EvidenceIds,
    [property: JsonPropertyName("guidanceIds")] IReadOnlyList<string>? GuidanceIds,
    [property: JsonPropertyName("acceptanceTests")] IReadOnlyList<AcceptanceTest>? AcceptanceTests,
    [property: JsonPropertyName("approvalRequired")] bool ApprovalRequired);

public sealed record AcceptanceTest(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("expectedOutcome")] string ExpectedOutcome);
