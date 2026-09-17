using System.Text.Json.Serialization;

namespace MigrationPlanner.Plan;

/// <summary>
/// Typed shape of <c>contracts/MigrationPlanV1.schema.json</c>. Every field
/// the schema declares required is populated by <see cref="PlanSkeletonBuilder"/>
/// from typed inputs, so schema validity is structural, not aspirational.
/// </summary>
public sealed record MigrationPlanV1
{
    [JsonPropertyName("schemaVersion")] public required string SchemaVersion { get; init; }
    [JsonPropertyName("planId")] public required string PlanId { get; init; }
    [JsonPropertyName("assessmentId")] public required string AssessmentId { get; init; }
    [JsonPropertyName("generatedAt")] public required string GeneratedAt { get; init; }
    [JsonPropertyName("modelProvenance")] public required ModelProvenance ModelProvenance { get; init; }
    [JsonPropertyName("scoreDigest")] public required string ScoreDigest { get; init; }
    [JsonPropertyName("corpusVersion")] public required string CorpusVersion { get; init; }
    [JsonPropertyName("recommendedPath")] public required string RecommendedPath { get; init; }
    [JsonPropertyName("confidence")] public required string Confidence { get; init; }
    [JsonPropertyName("executiveSummary")] public required string ExecutiveSummary { get; init; }
    [JsonPropertyName("scoreInterpretation")] public required string ScoreInterpretation { get; init; }
    [JsonPropertyName("facts")] public required IReadOnlyList<Statement> Facts { get; init; }
    [JsonPropertyName("inferences")] public required IReadOnlyList<Inference> Inferences { get; init; }
    [JsonPropertyName("alternatives")] public required IReadOnlyList<Alternative> Alternatives { get; init; }
    [JsonPropertyName("workItems")] public required IReadOnlyList<WorkItem> WorkItems { get; init; }
    [JsonPropertyName("missingSkills")] public required IReadOnlyList<MissingSkill> MissingSkills { get; init; }
    [JsonPropertyName("validationPlan")] public required ValidationPlan ValidationPlan { get; init; }
    [JsonPropertyName("risks")] public required IReadOnlyList<Risk> Risks { get; init; }
    [JsonPropertyName("unknowns")] public required IReadOnlyList<PlanUnknown> Unknowns { get; init; }
    [JsonPropertyName("requiredApprovals")] public required IReadOnlyList<Approval> RequiredApprovals { get; init; }
    [JsonPropertyName("reusableOutputs")] public required IReadOnlyList<ReusableOutput> ReusableOutputs { get; init; }
}

public sealed record ModelProvenance
{
    [JsonPropertyName("provider")] public required string Provider { get; init; }
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("version")] public required string Version { get; init; }
    [JsonPropertyName("temperature")] public double? Temperature { get; init; }
    [JsonPropertyName("promptDigest")] public string? PromptDigest { get; init; }
}

public sealed record Statement
{
    [JsonPropertyName("statement")] public required string Text { get; init; }
    [JsonPropertyName("evidenceIds")] public required IReadOnlyList<string> EvidenceIds { get; init; }
    [JsonPropertyName("guidanceIds")] public required IReadOnlyList<string> GuidanceIds { get; init; }
}

public sealed record Inference
{
    [JsonPropertyName("statement")] public required string Text { get; init; }
    [JsonPropertyName("evidenceIds")] public required IReadOnlyList<string> EvidenceIds { get; init; }
    [JsonPropertyName("guidanceIds")] public required IReadOnlyList<string> GuidanceIds { get; init; }
    [JsonPropertyName("confidence")] public required double Confidence { get; init; }
}

public sealed record Alternative
{
    [JsonPropertyName("path")] public required string Path { get; init; }
    [JsonPropertyName("disposition")] public required string Disposition { get; init; }
    [JsonPropertyName("rationale")] public required string Rationale { get; init; }
    [JsonPropertyName("evidenceIds")] public required IReadOnlyList<string> EvidenceIds { get; init; }
    [JsonPropertyName("guidanceIds")] public required IReadOnlyList<string> GuidanceIds { get; init; }
    [JsonPropertyName("estimatedEffort")] public string? EstimatedEffort { get; init; }
    [JsonPropertyName("risk")] public string? Risk { get; init; }
}

public sealed record WorkItem
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("sequence")] public required int Sequence { get; init; }
    [JsonPropertyName("priority")] public required string Priority { get; init; }
    [JsonPropertyName("title")] public required string Title { get; init; }
    [JsonPropertyName("objective")] public required string Objective { get; init; }
    [JsonPropertyName("agentOrSkill")] public required string AgentOrSkill { get; init; }
    [JsonPropertyName("inputs")] public required IReadOnlyList<string> Inputs { get; init; }
    [JsonPropertyName("expectedOutputs")] public required IReadOnlyList<string> ExpectedOutputs { get; init; }
    [JsonPropertyName("dependencies")] public required IReadOnlyList<string> Dependencies { get; init; }
    [JsonPropertyName("evidenceIds")] public required IReadOnlyList<string> EvidenceIds { get; init; }
    [JsonPropertyName("guidanceIds")] public required IReadOnlyList<string> GuidanceIds { get; init; }
    [JsonPropertyName("acceptanceTests")] public required IReadOnlyList<AcceptanceTest> AcceptanceTests { get; init; }
    [JsonPropertyName("approvalRequired")] public bool ApprovalRequired { get; init; } = true;
    [JsonPropertyName("estimatedEffort")] public required string EstimatedEffort { get; init; }
    [JsonPropertyName("risk")] public required string Risk { get; init; }
}

public sealed record AcceptanceTest
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("description")] public required string Description { get; init; }
    [JsonPropertyName("expectedOutcome")] public required string ExpectedOutcome { get; init; }
    [JsonPropertyName("evidenceIds")] public IReadOnlyList<string>? EvidenceIds { get; init; }
    [JsonPropertyName("guidanceIds")] public IReadOnlyList<string>? GuidanceIds { get; init; }
}

public sealed record MissingSkill
{
    [JsonPropertyName("proposedName")] public required string ProposedName { get; init; }
    [JsonPropertyName("purpose")] public required string Purpose { get; init; }
    [JsonPropertyName("requiredInputs")] public required IReadOnlyList<string> RequiredInputs { get; init; }
    [JsonPropertyName("expectedOutputs")] public required IReadOnlyList<string> ExpectedOutputs { get; init; }
    [JsonPropertyName("justification")] public required string Justification { get; init; }
    [JsonPropertyName("evidenceIds")] public required IReadOnlyList<string> EvidenceIds { get; init; }
    [JsonPropertyName("writeAccess")] public bool? WriteAccess { get; init; }
}

public sealed record ValidationPlan
{
    [JsonPropertyName("targetDevices")] public required IReadOnlyList<string> TargetDevices { get; init; }
    [JsonPropertyName("buildChecks")] public required IReadOnlyList<ValidationCheck> BuildChecks { get; init; }
    [JsonPropertyName("functionalChecks")] public required IReadOnlyList<ValidationCheck> FunctionalChecks { get; init; }
    [JsonPropertyName("reliabilityChecks")] public required IReadOnlyList<ValidationCheck> ReliabilityChecks { get; init; }
    [JsonPropertyName("performanceChecks")] public required IReadOnlyList<ValidationCheck> PerformanceChecks { get; init; }
    [JsonPropertyName("powerChecks")] public required IReadOnlyList<ValidationCheck> PowerChecks { get; init; }
    [JsonPropertyName("offlineChecks")] public required IReadOnlyList<ValidationCheck> OfflineChecks { get; init; }
    [JsonPropertyName("accessibilityChecks")] public required IReadOnlyList<ValidationCheck> AccessibilityChecks { get; init; }
    [JsonPropertyName("windowsExperienceChecks")] public required IReadOnlyList<ValidationCheck> WindowsExperienceChecks { get; init; }
}

public sealed record ValidationCheck
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("description")] public required string Description { get; init; }
    [JsonPropertyName("expectedOutcome")] public required string ExpectedOutcome { get; init; }
    [JsonPropertyName("evidenceIds")] public IReadOnlyList<string>? EvidenceIds { get; init; }
    [JsonPropertyName("guidanceIds")] public IReadOnlyList<string>? GuidanceIds { get; init; }
}

public sealed record Risk
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("description")] public required string Description { get; init; }
    [JsonPropertyName("severity")] public required string Severity { get; init; }
    [JsonPropertyName("mitigation")] public required string Mitigation { get; init; }
    [JsonPropertyName("evidenceIds")] public required IReadOnlyList<string> EvidenceIds { get; init; }
    [JsonPropertyName("guidanceIds")] public IReadOnlyList<string>? GuidanceIds { get; init; }
}

public sealed record PlanUnknown
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("description")] public required string Description { get; init; }
    [JsonPropertyName("requiredSkill")] public string? RequiredSkill { get; init; }
    [JsonPropertyName("evidenceIds")] public IReadOnlyList<string>? EvidenceIds { get; init; }
}

public sealed record Approval
{
    [JsonPropertyName("approvalId")] public required string ApprovalId { get; init; }
    [JsonPropertyName("summary")] public required string Summary { get; init; }
    [JsonPropertyName("workItemIds")] public required IReadOnlyList<string> WorkItemIds { get; init; }
}

public sealed record ReusableOutput
{
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("kind")] public required string Kind { get; init; }
    [JsonPropertyName("description")] public required string Description { get; init; }
}
