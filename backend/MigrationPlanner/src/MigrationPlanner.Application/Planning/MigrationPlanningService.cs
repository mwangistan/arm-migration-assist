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
    private readonly IPlanCache _planCache;

    public MigrationPlanningService(
        IEvidenceValidator evidenceValidator,
        IReadinessScorer scorer,
        IPlannerModel model,
        IPlanSchemaValidator planSchemaValidator,
        IPlanSafetyValidator safetyValidator,
        IWindowsOnArmGuidanceStore guidanceStore,
        IGuidanceLookupFactory guidanceLookupFactory,
        IAuditLogger auditLogger,
        IPlanCache planCache)
    {
        _evidenceValidator = evidenceValidator;
        _scorer = scorer;
        _model = model;
        _planSchemaValidator = planSchemaValidator;
        _safetyValidator = safetyValidator;
        _guidanceStore = guidanceStore;
        _guidanceLookupFactory = guidanceLookupFactory;
        _auditLogger = auditLogger;
        _planCache = planCache;
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

        var digest = ScoreDigest.Compute(score);
        if (_planCache.TryGet(digest, out var cached) && cached is not null)
        {
            EmitAudit(runId, assessment.AssessmentId, assessment.Repository.CommitSha, assessment.SchemaVersion,
                guidanceLookup.RetrievedGuidanceIds, "cache-hit", errorCode: null);
            var warnings = new List<string>
            {
                $"Cache hit: returned validated plan produced at {cached.StoredAt:O} for the same scoreDigest.",
            };
            warnings.AddRange(cached.Observations);
            return PlanResult.Ok(cached.Plan, cached.Score, runId, warnings);
        }

        var attempt1 = await AttemptAsync(assessment, score, guidanceLookup, retryHint: null, runId, cancellationToken)
            .ConfigureAwait(false);
        if (attempt1.ShapeErrors is not null)
        {
            var shapeHint = PlannerRetryHint.ForShapeInvalid(
                diagnostic: string.Join(" | ", attempt1.ShapeErrors),
                previousPlanJson: attempt1.RawJson ?? string.Empty);
            var shapeRetry = await AttemptAsync(
                assessment, score, guidanceLookup, shapeHint, runId, cancellationToken)
                .ConfigureAwait(false);
            if (shapeRetry.Failure is not null)
            {
                return shapeRetry.Failure;
            }
            attempt1 = shapeRetry;
        }
        else if (attempt1.Failure is not null)
        {
            return attempt1.Failure;
        }

        var plan = attempt1.Plan!;
        var safety1 = _safetyValidator.Validate(plan, assessment, score);
        if (safety1.IsSafe)
        {
            EmitAudit(runId, assessment.AssessmentId, assessment.Repository.CommitSha, assessment.SchemaVersion,
                guidanceLookup.RetrievedGuidanceIds, "success", errorCode: null);
            _planCache.Store(digest, plan, score, attempt1.Observations);
            return PlanResult.Ok(plan, score, runId, attempt1.Observations);
        }

        var retryHint = BuildRetryHint(safety1, plan, score, assessment, attempt1.RawJson ?? string.Empty);
        if (retryHint is not null)
        {
            var attempt2 = await AttemptAsync(assessment, score, guidanceLookup, retryHint, runId, cancellationToken)
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
                var warnings = new List<string> { BuildRetryWarning(retryHint) };
                warnings.AddRange(attempt1.Observations);
                warnings.AddRange(attempt2.Observations);
                _planCache.Store(digest, plan2, score, warnings);
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

    private static PlannerRetryHint? BuildRetryHint(
        PlanSafetyResult safety, MigrationPlanV1 plan, ReadinessScoreV1 score,
        Domain.Assessment.RepositoryAssessmentV1 assessment,
        string previousPlanJson)
    {
        var diagnostic = safety.Violations.Count > 0 ? safety.Violations[0] : string.Empty;

        if (safety.ErrorCode == PlannerErrorCode.PlanRecommendationInconsistent)
        {
            var (expectedPath, expectedConfidence) = RecommendationDispatch.Choose(score);
            return PlannerRetryHint.ForRecommendation(
                previousPath: ExtractString(plan, "recommendedPath") ?? "(unknown)",
                previousConfidence: ExtractString(plan, "confidence") ?? "(unknown)",
                expectedPath: expectedPath,
                expectedConfidence: expectedConfidence,
                diagnostic: diagnostic,
                previousPlanJson: previousPlanJson);
        }

        if (safety.ErrorCode == PlannerErrorCode.PlanMissingSkill)
        {
            var unresolved = ExtractUnresolvedSkills(plan, assessment);
            if (unresolved.Count == 0)
            {
                return null;
            }
            return PlannerRetryHint.ForMissingSkill(unresolved, diagnostic, previousPlanJson);
        }

        if (safety.ErrorCode == PlannerErrorCode.PlanEvidenceMissing)
        {
            var invalid = ExtractInvalidEvidenceIds(safety.Violations);
            var allowed = CollectAllowedEvidenceIds(assessment);
            if (invalid.Count == 0 || allowed.Count == 0)
            {
                return null;
            }
            return PlannerRetryHint.ForMissingEvidence(invalid, allowed, diagnostic, previousPlanJson);
        }

        if (safety.ErrorCode == PlannerErrorCode.PlanSkillIoMismatch)
        {
            var violations = ExtractSkillIoViolations(safety.Violations);
            if (violations.Count == 0)
            {
                return null;
            }
            var allowlists = CollectSkillIoAllowlists(plan, assessment, violations);
            if (allowlists.Count == 0)
            {
                return null;
            }
            return PlannerRetryHint.ForSkillIoMismatch(violations, allowlists, diagnostic, previousPlanJson);
        }

        return null;
    }

    private static IReadOnlyList<string> ExtractInvalidEvidenceIds(IReadOnlyList<string> violations)
    {
        var set = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var v in violations)
        {
            var start = v.IndexOf('\'');
            if (start < 0) continue;
            var end = v.IndexOf('\'', start + 1);
            if (end <= start) continue;
            var id = v.Substring(start + 1, end - start - 1);
            if (!string.IsNullOrWhiteSpace(id)) set.Add(id);
        }
        return set.ToArray();
    }

    private static IReadOnlyList<string> CollectAllowedEvidenceIds(
        Domain.Assessment.RepositoryAssessmentV1 assessment)
    {
        var set = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var d in assessment.Dependencies)
        {
            if (!string.IsNullOrWhiteSpace(d.EvidenceId)) set.Add(d.EvidenceId);
        }
        foreach (var f in assessment.CodeFindings)
        {
            if (!string.IsNullOrWhiteSpace(f.EvidenceId)) set.Add(f.EvidenceId);
        }
        if (!string.IsNullOrWhiteSpace(assessment.BuildFindings.EvidenceId))
        {
            set.Add(assessment.BuildFindings.EvidenceId);
        }
        foreach (var u in assessment.Unknowns)
        {
            if (u.EvidenceIds is null) continue;
            foreach (var eid in u.EvidenceIds)
            {
                if (!string.IsNullOrWhiteSpace(eid)) set.Add(eid);
            }
        }
        return set.ToArray();
    }

    // PlanSafetyValidator emits violations of the form:
    //   "Plan workItems[<idx>].<inputs|expectedOutputs> cites '<value>' which is not in
    //    <source> entry '<skillName>'. workItem <inputs|outputs> must be a subset of the
    //    skill's declared <inputs|outputs>."
    // Parse them back into structured violations the model can act on.
    private static IReadOnlyList<SkillIoViolation> ExtractSkillIoViolations(
        IReadOnlyList<string> violations)
    {
        var results = new List<SkillIoViolation>(violations.Count);
        foreach (var v in violations)
        {
            var openBracket = v.IndexOf('[');
            var closeBracket = v.IndexOf(']');
            if (openBracket < 0 || closeBracket <= openBracket) continue;
            if (!int.TryParse(v.AsSpan(openBracket + 1, closeBracket - openBracket - 1), out var idx))
                continue;

            var afterBracket = v.AsSpan(closeBracket + 1);
            string fieldName;
            if (afterBracket.StartsWith(".inputs"))
                fieldName = "inputs";
            else if (afterBracket.StartsWith(".expectedOutputs"))
                fieldName = "expectedOutputs";
            else
                continue;

            var firstQuote = v.IndexOf('\'', closeBracket);
            if (firstQuote < 0) continue;
            var secondQuote = v.IndexOf('\'', firstQuote + 1);
            if (secondQuote <= firstQuote) continue;
            var invalidValue = v.Substring(firstQuote + 1, secondQuote - firstQuote - 1);

            var entryLiteral = "entry '";
            var entryStart = v.IndexOf(entryLiteral, secondQuote + 1, StringComparison.Ordinal);
            if (entryStart < 0) continue;
            var skillNameStart = entryStart + entryLiteral.Length;
            var skillNameEnd = v.IndexOf('\'', skillNameStart);
            if (skillNameEnd <= skillNameStart) continue;
            var skillName = v.Substring(skillNameStart, skillNameEnd - skillNameStart);

            results.Add(new SkillIoViolation(idx, skillName, fieldName, invalidValue));
        }
        return results;
    }

    // The referenced skill can live in assessment.availableSkills OR plan.missingSkills;
    // collect the allowed input/output enumerations from whichever declared it so the model
    // has concrete replacements.
    private static IReadOnlyDictionary<string, SkillIoAllowlist> CollectSkillIoAllowlists(
        MigrationPlanV1 plan,
        Domain.Assessment.RepositoryAssessmentV1 assessment,
        IReadOnlyList<SkillIoViolation> violations)
    {
        var needed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var v in violations)
        {
            needed.Add(v.SkillName);
        }

        var result = new Dictionary<string, SkillIoAllowlist>(StringComparer.Ordinal);

        foreach (var s in assessment.AvailableSkills)
        {
            if (string.IsNullOrEmpty(s.Name) || !needed.Contains(s.Name)) continue;
            result[s.Name] = new SkillIoAllowlist(
                (s.SupportedInputs ?? Array.Empty<string>()).ToArray(),
                (s.SupportedOutputs ?? Array.Empty<string>()).ToArray());
        }

        if (plan.AdditionalProperties is not null &&
            plan.AdditionalProperties.TryGetValue("missingSkills", out var missingEl) &&
            missingEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in missingEl.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object) continue;
                if (!entry.TryGetProperty("proposedName", out var nameEl) ||
                    nameEl.ValueKind != JsonValueKind.String) continue;
                var name = nameEl.GetString();
                if (string.IsNullOrEmpty(name) || !needed.Contains(name) || result.ContainsKey(name)) continue;

                result[name] = new SkillIoAllowlist(
                    ReadStringArray(entry, "requiredInputs"),
                    ReadStringArray(entry, "expectedOutputs"));
            }
        }

        return result;
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement obj, string propertyName)
    {
        if (!obj.TryGetProperty(propertyName, out var el) || el.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();
        var list = new List<string>();
        foreach (var item in el.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var s = item.GetString();
                if (!string.IsNullOrEmpty(s)) list.Add(s);
            }
        }
        return list;
    }

    private static string BuildRetryWarning(PlannerRetryHint hint) => hint.Reason switch
    {
        PlannerRetryReason.RecommendationInconsistent =>
            $"Model recommendation corrected on retry: initial='{hint.PreviousRecommendedPath}' -> dispatch='{hint.ExpectedRecommendedPath}'.",
        PlannerRetryReason.SkillMissing =>
            $"Model declared previously-hallucinated skill(s) in missingSkills on retry: {string.Join(", ", hint.UnresolvedSkills ?? Array.Empty<string>())}.",
        PlannerRetryReason.ShapeInvalid =>
            "Model plan shape corrected on retry after JSON Schema failure.",
        PlannerRetryReason.MissingEvidence =>
            $"Model plan corrected on retry: invented evidenceId(s) {string.Join(", ", hint.InvalidEvidenceIds ?? Array.Empty<string>())} replaced.",
        PlannerRetryReason.SkillIoMismatch =>
            $"Model plan corrected on retry: {hint.SkillIoViolations?.Count ?? 0} workItem input/output value(s) re-mapped to the referenced skill's declared inputs/outputs.",
        _ => "Model plan corrected on retry.",
    };

    private static IReadOnlyList<string> ExtractUnresolvedSkills(
        MigrationPlanV1 plan, Domain.Assessment.RepositoryAssessmentV1 assessment)
    {
        var available = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in assessment.AvailableSkills)
        {
            if (!string.IsNullOrEmpty(s.Name)) available.Add(s.Name);
        }

        var declaredMissing = new HashSet<string>(StringComparer.Ordinal);
        if (plan.AdditionalProperties is not null
            && plan.AdditionalProperties.TryGetValue("missingSkills", out var missingElement)
            && missingElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in missingElement.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object) continue;
                if (entry.TryGetProperty("proposedName", out var proposedName)
                    && proposedName.ValueKind == JsonValueKind.String)
                {
                    var name = proposedName.GetString();
                    if (!string.IsNullOrEmpty(name)) declaredMissing.Add(name);
                }
            }
        }

        var unresolved = new SortedSet<string>(StringComparer.Ordinal);
        if (plan.AdditionalProperties is not null
            && plan.AdditionalProperties.TryGetValue("workItems", out var workItemsElement)
            && workItemsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var wi in workItemsElement.EnumerateArray())
            {
                if (wi.ValueKind != JsonValueKind.Object) continue;
                if (wi.TryGetProperty("agentOrSkill", out var skillElement)
                    && skillElement.ValueKind == JsonValueKind.String)
                {
                    var skill = skillElement.GetString() ?? string.Empty;
                    if (!string.IsNullOrEmpty(skill)
                        && !available.Contains(skill)
                        && !declaredMissing.Contains(skill))
                    {
                        unresolved.Add(skill);
                    }
                }
            }
        }
        return unresolved.ToArray();
    }

    private async Task<AttemptOutcome> AttemptAsync(
        Domain.Assessment.RepositoryAssessmentV1 assessment,
        ReadinessScoreV1 score,
        IGuidanceLookup guidanceLookup,
        PlannerRetryHint? retryHint,
        string runId,
        CancellationToken cancellationToken)
    {
        PlannerModelResult modelResult;
        try
        {
            modelResult = await _model
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
        catch (ModelRateLimitedException ex)
        {
            EmitAudit(runId, assessment.AssessmentId, assessment.Repository.CommitSha, assessment.SchemaVersion,
                guidanceLookup.RetrievedGuidanceIds, "model-rate-limited",
                PlannerErrorCode.ModelRateLimited);
            return AttemptOutcome.FromFailure(PlanResult.RateLimited(runId, ex.RetryAfter, ex.Message));
        }
        catch (Exception ex)
        {
            EmitAudit(runId, assessment.AssessmentId, assessment.Repository.CommitSha, assessment.SchemaVersion,
                guidanceLookup.RetrievedGuidanceIds, "model-failed", PlannerErrorCode.ModelFailed);
            return AttemptOutcome.FromFailure(PlanResult.Fail(PlannerErrorCode.ModelFailed, runId, ex.Message));
        }

        var planJson = modelResult.PlanJson;
        var observations = modelResult.Observations;

        MigrationPlanV1? plan;
        try
        {
            using var parsed = JsonDocument.Parse(planJson);

            var schemaResult = _planSchemaValidator.Validate(parsed.RootElement);
            if (!schemaResult.IsValid)
            {
                EmitAudit(runId, assessment.AssessmentId, assessment.Repository.CommitSha, assessment.SchemaVersion,
                    guidanceLookup.RetrievedGuidanceIds, "plan-shape-invalid", schemaResult.ErrorCode);
                var fallback = PlanResult.Fail(
                    schemaResult.ErrorCode ?? PlannerErrorCode.PlanShapeInvalid, runId,
                    [.. schemaResult.Errors]);
                return AttemptOutcome.FromShapeInvalid(planJson, schemaResult.Errors, fallback, observations);
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

        return AttemptOutcome.FromPlan(plan, planJson, observations);
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

    private sealed record AttemptOutcome(
        MigrationPlanV1? Plan,
        string? RawJson,
        PlanResult? Failure,
        IReadOnlyList<string>? ShapeErrors,
        IReadOnlyList<string> Observations)
    {
        public static AttemptOutcome FromPlan(MigrationPlanV1 plan, string rawJson, IReadOnlyList<string> observations) =>
            new(plan, rawJson, null, null, observations);
        public static AttemptOutcome FromFailure(PlanResult result) =>
            new(null, null, result, null, Array.Empty<string>());
        public static AttemptOutcome FromShapeInvalid(string rawJson, IReadOnlyList<string> errors, PlanResult fallback, IReadOnlyList<string> observations) =>
            new(null, rawJson, fallback, errors, observations);
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
