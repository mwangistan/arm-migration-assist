namespace Validation.BuildValidation;

public sealed class ValidationWorkflow(
    IRepositoryInspector repositoryInspector,
    IProcessRunner processRunner,
    IValidationPlanner? planner = null,
    IEvidenceAnalyzer? analyzer = null,
    ICoverageReviewer? reviewer = null,
    RunnerContext? runner = null)
{
    private static T Snapshot<T>(T value) => ValidationJson.Deserialize<T>(ValidationJson.Serialize(value));

    public async Task<PreparedValidation> PrepareAsync(
        MigrationPlan migration, RepositoryTarget target, PlanningOptions options, CancellationToken cancellationToken = default)
    {
        var repository = await repositoryInspector.InspectAsync(target, cancellationToken);
        var seed = new DeterministicPlanner().Prepare(migration, repository, options);
        if (planner is null)
            return seed with { Notices = seed.Notices.Append(new StageNotice("ai-planner", "No provider configured; deterministic/manual fallback used.")).ToArray() };
        try
        {
            var response = await planner.PlanAsync(Snapshot(new ValidationPlannerRequest(repository, migration, seed)), cancellationToken);
            var additions = response.AdditionalCommands.Select(command => new ValidationCommand(
                command.Id, command.Description, CommandKind.Custom, ExecutionSurface.Unspecified,
                command.Executable, command.Arguments, command.WorkingDirectory, command.Environment,
                command.TimeoutSeconds, command.DependsOn, command.CriterionKeys)).ToArray();
            var proposal = seed with
            {
                Commands = seed.Commands.Concat(additions).ToArray(),
                ManualChecks = seed.ManualChecks.Concat(response.ManualChecks).ToArray(),
                Notices = seed.Notices.Append(new StageNotice("ai-planner", response.Rationale)).ToArray()
            };
            proposal = DeterministicPlanner.ApplyMappings(proposal, response.Mappings);
            proposal = DeterministicPlanner.WithUnmappedManualChecks(proposal);
            PlanSafety.Validate(proposal);
            return Snapshot(proposal);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return seed with { Notices = seed.Notices.Append(new StageNotice("ai-planner",
                $"Provider failed or returned an invalid proposal; deterministic/manual fallback used: {ex.GetType().Name}")).ToArray() };
        }
    }

    public async Task<ValidationReport> RunAsync(
        PreparedValidation prepared, PlanApproval? approval, CancellationToken cancellationToken = default)
    {
        // Freeze the approved input before any provider can observe or mutate a request.
        var plan = Snapshot(prepared);
        var approved = approval is null ? null : Snapshot(approval);
        var started = DateTimeOffset.UtcNow;
        var execution = await new ValidationExecutor(repositoryInspector, processRunner, runner)
            .ExecuteAsync(plan, approved, cancellationToken);
        var scorecard = ScorecardBuilder.Build(plan, execution.Results);
        var gaps = ScorecardBuilder.Coverage(plan, scorecard, execution.Results);
        var notices = new List<StageNotice>(plan.Notices);
        EvidenceAnalysisResponse? evidenceAnalysis = null;
        CoverageReviewResponse? coverageReview = null;
        var evidenceIds = execution.Evidence.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var commandIds = plan.Commands.Select(command => command.Id).ToHashSet(StringComparer.Ordinal);
        var criterionKeys = plan.Criteria.Select(criterion => criterion.Key).ToHashSet(StringComparer.Ordinal);

        if (analyzer is not null && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                var approvedCommands = plan.Commands.Where(command => approved is not null &&
                    approved.PlanFingerprint == PlanSafety.Fingerprint(plan) && approved.ApprovedCommandIds.Contains(command.Id)).ToArray();
                var response = await analyzer.AnalyzeAsync(Snapshot(new EvidenceAnalysisRequest(
                    plan.Repository, approvedCommands, execution.Results, execution.Evidence)), cancellationToken);
                PlanSafety.Require(response.RootCauses.All(cause => ValidConfidence(cause.Confidence) &&
                    !string.IsNullOrWhiteSpace(cause.Diagnosis) && !string.IsNullOrWhiteSpace(cause.Rationale) &&
                    cause.EvidenceIds.Count > 0 && cause.EvidenceIds.All(evidenceIds.Contains) &&
                    cause.CommandIds.All(commandIds.Contains)), "Invalid AI evidence references or confidence.");
                evidenceAnalysis = Snapshot(response);
            }
            catch (Exception ex)
            {
                notices.Add(new("ai-evidence-analyzer", $"Analysis unavailable/invalid ({ex.GetType().Name}); deterministic results preserved."));
            }
        }
        else notices.Add(new("ai-evidence-analyzer", "No analysis performed; provider absent or run cancelled."));

        if (reviewer is not null && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                var response = await reviewer.ReviewAsync(Snapshot(new CoverageReviewRequest(
                    plan.TargetDevices, scorecard, gaps, execution.Evidence)), cancellationToken);
                PlanSafety.Require(response.Recommendations.All(recommendation => ValidConfidence(recommendation.Confidence) &&
                    !string.IsNullOrWhiteSpace(recommendation.Description) && !string.IsNullOrWhiteSpace(recommendation.Rationale) &&
                    recommendation.CriterionKeys.All(criterionKeys.Contains) &&
                    recommendation.EvidenceIds.All(evidenceIds.Contains)), "Invalid AI coverage references or confidence.");
                coverageReview = Snapshot(response);
            }
            catch (Exception ex)
            {
                notices.Add(new("ai-coverage-reviewer", $"Review unavailable/invalid ({ex.GetType().Name}); coverage gaps preserved."));
            }
        }
        else notices.Add(new("ai-coverage-reviewer", "No review performed; provider absent or run cancelled."));

        return new("1.0", Guid.NewGuid().ToString("N"), PlanSafety.Fingerprint(plan), plan.MigrationPlanId,
            plan.Repository, execution.Runner, started, DateTimeOffset.UtcNow, execution.Results, execution.Evidence,
            scorecard, gaps, new(evidenceAnalysis, coverageReview, notices));
    }

    private static bool ValidConfidence(double confidence) => double.IsFinite(confidence) && confidence is >= 0 and <= 1;
}
