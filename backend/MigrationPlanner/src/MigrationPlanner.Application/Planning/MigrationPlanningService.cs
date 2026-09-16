using System.Text.Json;
using MigrationPlanner.Application.Abstractions;
using MigrationPlanner.Application.Auditing;
using MigrationPlanner.Application.Mcp;
using MigrationPlanner.Domain.Errors;
using MigrationPlanner.Domain.Plan;

namespace MigrationPlanner.Application.Planning;

/// <summary>
/// Orchestrates the read-only planning flow: evidence validation, deterministic
/// scoring, model invocation, plan safety validation, and audit emission. Never
/// mutates any repository; all failures produce stable error codes.
/// </summary>
public sealed class MigrationPlanningService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null,
    };

    private readonly IEvidenceValidator _evidenceValidator;
    private readonly IReadinessScorer _scorer;
    private readonly IPlannerModel _model;
    private readonly IPlanSchemaValidator _planSchemaValidator;
    private readonly IPlanSafetyValidator _safetyValidator;
    private readonly IWindowsOnArmGuidanceStore _guidanceStore;
    private readonly IGuidanceLookupFactory _guidanceLookupFactory;
    private readonly IAuditLogger _auditLogger;

    public MigrationPlanningService(
        IEvidenceValidator evidenceValidator,
        IReadinessScorer scorer,
        IPlannerModel model,
        IPlanSchemaValidator planSchemaValidator,
        IPlanSafetyValidator safetyValidator,
        IWindowsOnArmGuidanceStore guidanceStore,
        IGuidanceLookupFactory guidanceLookupFactory,
        IAuditLogger auditLogger)
    {
        _evidenceValidator = evidenceValidator;
        _scorer = scorer;
        _model = model;
        _planSchemaValidator = planSchemaValidator;
        _safetyValidator = safetyValidator;
        _guidanceStore = guidanceStore;
        _guidanceLookupFactory = guidanceLookupFactory;
        _auditLogger = auditLogger;
    }

    public async Task<PlanResult> PlanAsync(PlanRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var runId = request.RunId ?? Guid.NewGuid().ToString("N");
        var assessment = request.Assessment;

        var evidenceResult = _evidenceValidator.Validate(assessment);
        if (!evidenceResult.IsValid)
        {
            EmitAudit(runId, assessment.AssessmentId, assessment.Repository.CommitSha, assessment.SchemaVersion,
                Array.Empty<string>(), "invalid-evidence", evidenceResult.ErrorCode);
            return PlanResult.Fail(evidenceResult.ErrorCode ?? PlannerErrorCode.EvidenceInvalid, runId,
                [.. evidenceResult.Errors]);
        }

        var score = _scorer.Score(assessment);
        var guidanceLookup = _guidanceLookupFactory.Create();

        var attempt1 = await AttemptAsync(assessment, score, guidanceLookup, retryHint: null, runId, cancellationToken)
            .ConfigureAwait(false);
        if (attempt1.Failure is not null)
        {
            return attempt1.Failure;
        }

        var plan = attempt1.Plan!;
        var safety1 = _safetyValidator.Validate(plan, assessment, score);
        if (safety1.IsSafe)
        {
            EmitAudit(runId, assessment.AssessmentId, assessment.Repository.CommitSha, assessment.SchemaVersion,
                guidanceLookup.RetrievedGuidanceIds, "success", errorCode: null);
            return PlanResult.Ok(plan, score, runId, Array.Empty<string>());
        }

        if (safety1.ErrorCode == PlannerErrorCode.PlanRecommendationInconsistent)
        {
            var (expectedPath, expectedConfidence) = RecommendationDispatch.Choose(score);
            var previousPath = ExtractString(plan, "recommendedPath") ?? "(unknown)";
            var previousConfidence = ExtractString(plan, "confidence") ?? "(unknown)";
            var hint = new PlannerRetryHint(
                PreviousRecommendedPath: previousPath,
                PreviousConfidence: previousConfidence,
                ExpectedRecommendedPath: expectedPath,
                ExpectedConfidence: expectedConfidence,
                Diagnostic: safety1.Violations.Count > 0 ? safety1.Violations[0] : string.Empty);

            var attempt2 = await AttemptAsync(assessment, score, guidanceLookup, hint, runId, cancellationToken)
                .ConfigureAwait(false);
            if (attempt2.Failure is not null)
            {
                return attempt2.Failure;
            }

            var plan2 = attempt2.Plan!;
            var safety2 = _safetyValidator.Validate(plan2, assessment, score);
            if (safety2.IsSafe)
            {
                EmitAudit(runId, assessment.AssessmentId, assessment.Repository.CommitSha, assessment.SchemaVersion,
                    guidanceLookup.RetrievedGuidanceIds, "success-after-retry", errorCode: null);
                var warnings = new[]
                {
                    $"Model recommendation corrected on retry: initial='{previousPath}' -> dispatch='{expectedPath}'.",
                };
                return PlanResult.Ok(plan2, score, runId, warnings);
            }

            EmitAudit(runId, assessment.AssessmentId, assessment.Repository.CommitSha, assessment.SchemaVersion,
                guidanceLookup.RetrievedGuidanceIds, "unsafe-plan-after-retry", safety2.ErrorCode);
            return PlanResult.Fail(safety2.ErrorCode ?? PlannerErrorCode.PlanSafetyViolation, runId,
                [.. safety2.Violations]);
        }

        EmitAudit(runId, assessment.AssessmentId, assessment.Repository.CommitSha, assessment.SchemaVersion,
            guidanceLookup.RetrievedGuidanceIds, "unsafe-plan", safety1.ErrorCode);
        return PlanResult.Fail(safety1.ErrorCode ?? PlannerErrorCode.PlanSafetyViolation, runId,
            [.. safety1.Violations]);
    }

    private async Task<AttemptOutcome> AttemptAsync(
        Domain.Assessment.RepositoryAssessmentV1 assessment,
        ReadinessScoreV1 score,
        IGuidanceLookup guidanceLookup,
        PlannerRetryHint? retryHint,
        string runId,
        CancellationToken cancellationToken)
    {
        string planJson;
        try
        {
            planJson = await _model
                .GeneratePlanJsonAsync(assessment, score, guidanceLookup, cancellationToken, retryHint)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (GuidanceLookupBudgetExceededException ex)
        {
            EmitAudit(runId, assessment.AssessmentId, assessment.Repository.CommitSha, assessment.SchemaVersion,
                guidanceLookup.RetrievedGuidanceIds, "model-tool-budget-exceeded",
                PlannerErrorCode.ModelToolBudgetExceeded);
            return AttemptOutcome.FromFailure(PlanResult.Fail(
                PlannerErrorCode.ModelToolBudgetExceeded, runId, ex.Message));
        }
        catch (UnknownMcpToolException ex)
        {
            EmitAudit(runId, assessment.AssessmentId, assessment.Repository.CommitSha, assessment.SchemaVersion,
                guidanceLookup.RetrievedGuidanceIds, "model-tool-violation",
                PlannerErrorCode.ModelToolViolation);
            return AttemptOutcome.FromFailure(PlanResult.Fail(
                PlannerErrorCode.ModelToolViolation, runId, ex.Message));
        }
        catch (Exception ex)
        {
            EmitAudit(runId, assessment.AssessmentId, assessment.Repository.CommitSha, assessment.SchemaVersion,
                guidanceLookup.RetrievedGuidanceIds, "model-failed", PlannerErrorCode.ModelFailed);
            return AttemptOutcome.FromFailure(PlanResult.Fail(PlannerErrorCode.ModelFailed, runId, ex.Message));
        }

        MigrationPlanV1? plan;
        try
        {
            using var parsed = JsonDocument.Parse(planJson);

            var schemaResult = _planSchemaValidator.Validate(parsed.RootElement);
            if (!schemaResult.IsValid)
            {
                EmitAudit(runId, assessment.AssessmentId, assessment.Repository.CommitSha, assessment.SchemaVersion,
                    guidanceLookup.RetrievedGuidanceIds, "plan-shape-invalid", schemaResult.ErrorCode);
                return AttemptOutcome.FromFailure(PlanResult.Fail(
                    schemaResult.ErrorCode ?? PlannerErrorCode.PlanShapeInvalid, runId,
                    [.. schemaResult.Errors]));
            }

            plan = parsed.RootElement.Deserialize<MigrationPlanV1>(JsonOptions);
        }
        catch (JsonException ex)
        {
            EmitAudit(runId, assessment.AssessmentId, assessment.Repository.CommitSha, assessment.SchemaVersion,
                guidanceLookup.RetrievedGuidanceIds, "invalid-model-output", PlannerErrorCode.ModelOutputInvalid);
            return AttemptOutcome.FromFailure(PlanResult.Fail(
                PlannerErrorCode.ModelOutputInvalid, runId, ex.Message));
        }

        if (plan is null || plan.AssessmentId != assessment.AssessmentId)
        {
            EmitAudit(runId, assessment.AssessmentId, assessment.Repository.CommitSha, assessment.SchemaVersion,
                guidanceLookup.RetrievedGuidanceIds, "invalid-model-output", PlannerErrorCode.ModelOutputInvalid);
            return AttemptOutcome.FromFailure(PlanResult.Fail(
                PlannerErrorCode.ModelOutputInvalid, runId,
                "Model did not echo the requested assessmentId."));
        }

        return AttemptOutcome.FromPlan(plan);
    }

    private static string? ExtractString(MigrationPlanV1 plan, string key)
    {
        if (plan.AdditionalProperties is null ||
            !plan.AdditionalProperties.TryGetValue(key, out var element) ||
            element.ValueKind != JsonValueKind.String)
        {
            return null;
        }
        return element.GetString();
    }

    private sealed record AttemptOutcome(MigrationPlanV1? Plan, PlanResult? Failure)
    {
        public static AttemptOutcome FromPlan(MigrationPlanV1 plan) => new(plan, null);
        public static AttemptOutcome FromFailure(PlanResult result) => new(null, result);
    }

    private void EmitAudit(
        string runId,
        string assessmentId,
        string commitSha,
        string schemaVersion,
        IReadOnlyCollection<string> guidanceIdsRetrieved,
        string outcome,
        string? errorCode)
    {
        _auditLogger.Record(new AuditEvent(
            RunId: runId,
            AssessmentId: assessmentId,
            CommitSha: commitSha,
            SchemaVersion: schemaVersion,
            ModelProvider: _model.GetType().Name,
            ModelName: _model.GetType().FullName ?? _model.GetType().Name,
            CorpusVersion: _guidanceStore.CorpusVersion,
            McpToolsInvoked: Array.Empty<string>(),
            EvidenceIdsAccessed: Array.Empty<string>(),
            GuidanceIdsRetrieved: guidanceIdsRetrieved,
            ValidationResult: outcome,
            ErrorCode: errorCode,
            OccurredAt: DateTimeOffset.UtcNow));
    }
}

