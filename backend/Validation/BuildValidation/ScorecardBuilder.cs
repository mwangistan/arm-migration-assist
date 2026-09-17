namespace Validation.BuildValidation;

public static class ScorecardBuilder
{
    public static Scorecard Build(PreparedValidation plan, IReadOnlyList<CommandResult> results)
    {
        var criteria = plan.Criteria.Select(criterion =>
        {
            var ids = plan.Commands.Where(command => command.CriterionKeys.Contains(criterion.Key))
                .Select(command => command.Id).ToArray();
            var mapped = ids.Select(id => results.SingleOrDefault(result => result.CommandId == id) ??
                new CommandResult(id, ResultStatus.NotRun, "No execution result exists.", [])).ToArray();
            var status = Aggregate(mapped.Select(result => result.Status).ToArray());
            return new CriterionResult(criterion, status,
                mapped.Length == 0 ? "No executable mapping or measured evidence; manual/untested criteria remain not-run." :
                string.Join(" ", mapped.Select(result => $"{result.CommandId}: {result.Reason}")),
                ids, mapped.SelectMany(result => result.EvidenceIds).Distinct(StringComparer.Ordinal).ToArray());
        }).ToArray();
        int Count(ResultStatus status) => criteria.Count(result => result.Status == status);
        var overall = Count(ResultStatus.Failed) > 0 ? OverallStatus.ValidationFailed :
            criteria.Length > 0 && Count(ResultStatus.Passed) == criteria.Length ? OverallStatus.Validated :
            Count(ResultStatus.Passed) > 0 ? OverallStatus.PartiallyValidated : OverallStatus.NotValidated;
        return new(overall, Count(ResultStatus.Passed), Count(ResultStatus.Failed), Count(ResultStatus.NotRun),
            Count(ResultStatus.Inconclusive), Count(ResultStatus.Skipped), criteria);
    }

    private static ResultStatus Aggregate(IReadOnlyList<ResultStatus> statuses)
    {
        if (statuses.Count == 0) return ResultStatus.NotRun;
        if (statuses.Contains(ResultStatus.Failed)) return ResultStatus.Failed;
        if (statuses.Contains(ResultStatus.Inconclusive)) return ResultStatus.Inconclusive;
        if (statuses.All(status => status == ResultStatus.Passed)) return ResultStatus.Passed;
        if (statuses.All(status => status == ResultStatus.Skipped)) return ResultStatus.Skipped;
        if (statuses.All(status => status == ResultStatus.NotRun)) return ResultStatus.NotRun;
        return ResultStatus.Inconclusive;
    }

    public static IReadOnlyList<CoverageGap> Coverage(PreparedValidation plan, Scorecard scorecard, IReadOnlyList<CommandResult> results)
    {
        var gaps = scorecard.Criteria.Where(result => result.Status != ResultStatus.Passed)
            .Select(result => new CoverageGap(result.Criterion.Key,
                $"{result.Status}: {result.Criterion.Description}", [result.Criterion.Key])).ToList();
        bool HasRuntime(ExecutionSurface surface) => plan.Commands.Any(command => command.Surface == surface &&
            command.Kind is CommandKind.DotNetTest or CommandKind.NativeSmoke or CommandKind.ContainerSmoke &&
            results.Any(result => result.CommandId == command.Id && result.Status == ResultStatus.Passed));
        if (!HasRuntime(ExecutionSurface.WindowsArm64Runtime))
            gaps.Add(new("missing-windows-arm64-runtime", "No successful Windows ARM64 runtime evidence. Builds and Linux containers do not establish Windows on Arm readiness.", []));
        if (!plan.TargetDevices.Contains("x64-baseline-for-comparison"))
            gaps.Add(new("missing-x64-comparison", "No x64 baseline was requested or compared; no performance/regression parity claim can be made.", []));
        if (plan.Commands.Any(command => command.Surface == ExecutionSurface.LinuxArm64Container))
            gaps.Add(new("container-scope", "Linux ARM64 container validation only; execution may be emulated. It does not prove Windows UX, physical ARM64 hardware, power or performance.", []));
        return gaps;
    }
}
