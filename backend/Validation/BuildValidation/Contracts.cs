using System.Text.Json;
using System.Text.Json.Serialization;

namespace Validation.BuildValidation;

// Feature 4's read-only subset of MigrationPlanV1; Feature 2 owns the full schema.
public sealed record MigrationPlan(
    string SchemaVersion,
    string PlanId,
    ValidationPlan ValidationPlan,
    IReadOnlyList<WorkItem> WorkItems);

public sealed record WorkItem(string Id, IReadOnlyList<AcceptanceTest> AcceptanceTests);
public sealed record AcceptanceTest(string Id, string Description, string ExpectedOutcome);
public sealed record ValidationCheck(
    string Id, string Description, string ExpectedOutcome,
    IReadOnlyList<string>? EvidenceIds = null, IReadOnlyList<string>? GuidanceIds = null);

public sealed record ValidationPlan(
    IReadOnlyList<string> TargetDevices,
    IReadOnlyList<ValidationCheck> BuildChecks,
    IReadOnlyList<ValidationCheck> FunctionalChecks,
    IReadOnlyList<ValidationCheck> ReliabilityChecks,
    IReadOnlyList<ValidationCheck> PerformanceChecks,
    IReadOnlyList<ValidationCheck> PowerChecks,
    IReadOnlyList<ValidationCheck> OfflineChecks,
    IReadOnlyList<ValidationCheck> AccessibilityChecks,
    IReadOnlyList<ValidationCheck> WindowsExperienceChecks)
{
    public IEnumerable<(string Category, ValidationCheck Check)> Checks() =>
        new (string, IReadOnlyList<ValidationCheck>)[]
        {
            ("build", BuildChecks), ("functional", FunctionalChecks), ("reliability", ReliabilityChecks),
            ("performance", PerformanceChecks), ("power", PowerChecks), ("offline", OfflineChecks),
            ("accessibility", AccessibilityChecks), ("windowsExperience", WindowsExperienceChecks)
        }.SelectMany(group => group.Item2.Select(check => (group.Item1, check)));
}

public enum ResultStatus { Passed, Failed, NotRun, Inconclusive, Skipped }
public enum OverallStatus { Validated, ValidationFailed, PartiallyValidated, NotValidated }
public enum CheckSource { ValidationCheck, AcceptanceTest, Discovered }
public enum CommandKind { DotNetBuild, DotNetTest, NativeBuild, NativeSmoke, ContainerBuild, ContainerInspect, ContainerSmoke, Custom }
public enum ExecutionSurface { WindowsArm64Build, WindowsArm64Runtime, LinuxArm64Container, Unspecified }
public enum ProofKind { ExitCode, Trx, ContainerArchitecture }

public sealed record Criterion(
    string Key, CheckSource Source, string SourceId, string? WorkItemId,
    string Category, string Description, string ExpectedOutcome,
    IReadOnlyList<string> SourceEvidenceIds, IReadOnlyList<string> GuidanceIds);

public sealed record RepositoryTarget(string Path, string? CommitSha = null, string? Branch = null);
public sealed record RepositoryArtifact(string Path, string Sha256, string Content);
public sealed record RepositoryContext(
    string RootPath, string CommitSha, string? Branch,
    IReadOnlyList<RepositoryArtifact> Artifacts, IReadOnlyList<string> Notices);
public sealed record RunnerContext(string OperatingSystem, string Architecture, string MachineName);

public sealed record ValidationCommand(
    string Id,
    string Description,
    CommandKind Kind,
    ExecutionSurface Surface,
    string Executable,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string> Environment,
    int TimeoutSeconds,
    IReadOnlyList<string> DependsOn,
    IReadOnlyList<string> CriterionKeys,
    ProofKind Proof = ProofKind.ExitCode,
    string? ProofPath = null);

// Smoke commands are never inferred from ENTRYPOINT, project names, or prose.
public sealed record NativeSmokeCheck(
    string ProjectPath, string Executable, IReadOnlyList<string> Arguments,
    IReadOnlyList<string> CriterionKeys);
public sealed record ContainerSmokeCheck(
    string DockerfilePath, IReadOnlyList<string> Arguments, IReadOnlyList<string> CriterionKeys);
public sealed record CommandMapping(string CommandId, IReadOnlyList<string> CriterionKeys);
public sealed record PlanningOptions(
    string EvidenceDirectory,
    IReadOnlyList<CommandMapping>? Mappings = null,
    IReadOnlyList<NativeSmokeCheck>? NativeSmokeChecks = null,
    IReadOnlyList<ContainerSmokeCheck>? ContainerSmokeChecks = null);
public sealed record ManualCheck(string CriterionKey, string Instructions, string Reason);
public sealed record StageNotice(string Stage, string Message);

public sealed record PreparedValidation(
    string SchemaVersion,
    string PlanId,
    string MigrationPlanId,
    RepositoryContext Repository,
    string EvidenceDirectory,
    IReadOnlyList<string> TargetDevices,
    IReadOnlyList<Criterion> Criteria,
    IReadOnlyList<ValidationCommand> Commands,
    IReadOnlyList<ManualCheck> ManualChecks,
    IReadOnlyList<StageNotice> Notices);

// Approval is issued by a caller/UI, never by an AI stage. Empty input approves nothing.
public sealed record PlanApproval(
    string PlanFingerprint, IReadOnlyList<string> ApprovedCommandIds,
    IReadOnlyDictionary<string, string>? SkippedCommands = null);

public sealed record ProcessInvocation(
    string Executable, IReadOnlyList<string> Arguments, string WorkingDirectory,
    IReadOnlyDictionary<string, string> Environment, int TimeoutSeconds);
public sealed record ProcessOutcome(
    DateTimeOffset StartedAt, DateTimeOffset FinishedAt, int? ExitCode,
    string StandardOutput, string StandardError, string? StartError = null,
    bool TimedOut = false, bool Cancelled = false, bool OutputTruncated = false, bool OutputIncomplete = false);
public sealed record ArtifactReference(string Path, string Sha256, long SizeBytes);
public sealed record ExecutionEvidence(
    string Id, string? CommandId, string Kind, ProcessInvocation? Invocation,
    ProcessOutcome? Process, string Summary, IReadOnlyList<ArtifactReference> Artifacts);
public sealed record CommandResult(
    string CommandId, ResultStatus Status, string Reason, IReadOnlyList<string> EvidenceIds);
public sealed record CriterionResult(
    Criterion Criterion, ResultStatus Status, string Reason,
    IReadOnlyList<string> CommandIds, IReadOnlyList<string> EvidenceIds);
public sealed record Scorecard(
    OverallStatus Status, int Passed, int Failed, int NotRun, int Inconclusive, int Skipped,
    IReadOnlyList<CriterionResult> Criteria);
public sealed record CoverageGap(string Id, string Description, IReadOnlyList<string> CriterionKeys);

// AI analysis is a separate channel. These contracts deliberately have no result-status field.
public sealed record RootCauseAnalysis(
    string Id, string Diagnosis, string Rationale, double Confidence,
    IReadOnlyList<string> CommandIds, IReadOnlyList<string> EvidenceIds);
public sealed record EvidenceAnalysisResponse(IReadOnlyList<RootCauseAnalysis> RootCauses);
public sealed record CoverageRecommendation(
    string Description, string Rationale, double Confidence,
    IReadOnlyList<string> CriterionKeys, IReadOnlyList<string> EvidenceIds);
public sealed record CoverageReviewResponse(IReadOnlyList<CoverageRecommendation> Recommendations);
public sealed record AiAnalysis(
    EvidenceAnalysisResponse? EvidenceAnalysis, CoverageReviewResponse? CoverageReview,
    IReadOnlyList<StageNotice> Notices);
public sealed record ValidationReport(
    string SchemaVersion, string RunId, string PlanFingerprint, string MigrationPlanId,
    RepositoryContext Repository, RunnerContext Runner,
    DateTimeOffset StartedAt, DateTimeOffset FinishedAt,
    IReadOnlyList<CommandResult> Commands, IReadOnlyList<ExecutionEvidence> Evidence,
    Scorecard Scorecard, IReadOnlyList<CoverageGap> CoverageGaps, AiAnalysis Ai);

public static class ValidationJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower, allowIntegerValues: false) }
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, Options) ?? throw new InvalidDataException("JSON must not be null.");
}
