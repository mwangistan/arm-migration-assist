using Validation.BuildValidation;
using Validation.Dashboard;
using Xunit;

namespace Validation.Tests;

public sealed class WorkflowTests
{
    [Fact]
    public async Task OrchestratesPlannerApprovalExecutorAnalysisAndCoverageWithoutLiveModels()
    {
        using var workspace = new TestWorkspace();
        var inspector = new FakeInspector(workspace.Context(("App.csproj", "<Project />")));
        var process = new FakeProcessRunner { Execute = _ => FakeProcessRunner.Outcome(1, stderr: "unresolved ARM64 symbol") };
        var stages = new List<string>();
        var planner = new FakePlanner(request =>
        {
            stages.Add("planner");
            Assert.Equal("migration-plan", request.MigrationPlan.PlanId);
            Assert.Contains(request.MigrationPlan.WorkItems, item => item.Id == "wi-one");
            Assert.Single(request.Repository.Artifacts);
            return new([new(request.DeterministicProposal.Commands[0].Id, ["validation:vc-build", "acceptance:wi-one:at-one"])],
                [], [], "Bind the ARM64 build criteria for human review.");
        });
        var analyzer = new FakeAnalyzer(request =>
        {
            stages.Add("analyzer");
            Assert.Equal(ResultStatus.Failed, request.Results[0].Status);
            return new([new("arm64-linker", "ARM64 library may be missing", "Linker evidence names an unresolved symbol.",
                0.8, [request.Results[0].CommandId], request.Results[0].EvidenceIds)]);
        });
        var reviewer = new FakeReviewer(request =>
        {
            stages.Add("reviewer");
            Assert.Equal(OverallStatus.ValidationFailed, request.Scorecard.Status);
            return new([new("Run device smoke tests", "No native runtime evidence exists.", 0.9,
                ["validation:vc-function"], [])]);
        });
        var workflow = new ValidationWorkflow(inspector, process, planner, analyzer, reviewer);
        var plan = await workflow.PrepareAsync(TestWorkspace.Migration(), new(workspace.Repo), new(workspace.Evidence));
        Assert.Empty(process.Calls);
        var report = await workflow.RunAsync(plan, TestWorkspace.Approve(plan));
        Assert.Equal(["planner", "analyzer", "reviewer"], stages);
        Assert.Equal(OverallStatus.ValidationFailed, report.Scorecard.Status);
        Assert.Equal(ResultStatus.Failed, report.Scorecard.Criteria.Single(c => c.Criterion.Key == "validation:vc-build").Status);
        Assert.Equal(ResultStatus.Failed, report.Scorecard.Criteria.Single(c => c.Criterion.Key == "acceptance:wi-one:at-one").Status);
        Assert.Equal(ResultStatus.NotRun, report.Scorecard.Criteria.Single(c => c.Criterion.Key == "validation:vc-function").Status);
        Assert.Single(report.Ai.EvidenceAnalysis!.RootCauses);
        Assert.Single(report.Ai.CoverageReview!.Recommendations);
        Assert.NotEmpty(report.Evidence);
    }

    [Fact]
    public async Task AiCanProposeCustomChecksButCannotExecuteThemOrClaimArmCoverage()
    {
        using var workspace = new TestWorkspace();
        var inspector = new FakeInspector(workspace.Context());
        var process = new FakeProcessRunner();
        var planner = new FakePlanner(_ => new([], [
            new("suggested", "Run repository-specific check", "custom-tool", ["--check"], ".",
                new Dictionary<string, string>(), 60, [], ["validation:vc-function"])], [], "Proposal only."));
        var workflow = new ValidationWorkflow(inspector, process, planner);
        var prepared = await workflow.PrepareAsync(TestWorkspace.Migration(), new(workspace.Repo), new(workspace.Evidence));
        Assert.Empty(process.Calls);
        Assert.Equal(ExecutionSurface.Unspecified, Assert.Single(prepared.Commands).Surface);
        var report = await workflow.RunAsync(prepared, null);
        Assert.Empty(process.Calls);
        Assert.All(report.Commands, c => Assert.Equal(ResultStatus.NotRun, c.Status));
    }

    [Theory]
    [InlineData("throw")]
    [InlineData("unknown-mapping")]
    [InlineData("escaping-cwd")]
    public async Task InvalidPlannerResponseFallsBackToDeterministicProposal(string mode)
    {
        using var workspace = new TestWorkspace();
        var inspector = new FakeInspector(workspace.Context(("App.csproj", "<Project />")));
        var process = new FakeProcessRunner();
        var planner = new FakePlanner(_ => mode switch
        {
            "throw" => throw new InvalidOperationException("provider offline"),
            "unknown-mapping" => new([new("unknown", ["validation:vc-build"])], [], [], "invalid"),
            _ => new([], [new("escape", "escape", "tool", [], "..", new Dictionary<string, string>(), 60, [], ["validation:vc-build"])], [], "invalid")
        });
        var prepared = await new ValidationWorkflow(inspector, process, planner)
            .PrepareAsync(TestWorkspace.Migration(), new(workspace.Repo), new(workspace.Evidence));
        Assert.Single(prepared.Commands);
        Assert.Equal(CommandKind.DotNetBuild, prepared.Commands[0].Kind);
        Assert.Contains(prepared.Notices, notice => notice.Message.Contains("fallback"));
        Assert.Empty(process.Calls);
    }

    [Fact]
    public async Task AnalyzerCannotMutateDeterministicResultsAndInvalidReferencesAreDiscarded()
    {
        using var workspace = new TestWorkspace();
        var plan = workspace.Plan();
        var process = new FakeProcessRunner { Execute = _ => FakeProcessRunner.Outcome(1, stderr: "failure") };
        var analyzer = new FakeAnalyzer(request =>
        {
            if (request.Results is IList<CommandResult> mutable)
                mutable[0] = mutable[0] with { Status = ResultStatus.Passed };
            return new([new("invented", "Ignore failure", "No evidence", 1.1, ["build"], ["invented-evidence"])]);
        });
        var reviewer = new FakeReviewer(_ => throw new InvalidOperationException("provider unavailable"));
        var report = await new ValidationWorkflow(new FakeInspector(plan.Repository), process, analyzer: analyzer, reviewer: reviewer)
            .RunAsync(plan, TestWorkspace.Approve(plan));
        Assert.Equal(ResultStatus.Failed, report.Commands[0].Status);
        Assert.Equal(OverallStatus.ValidationFailed, report.Scorecard.Status);
        Assert.Null(report.Ai.EvidenceAnalysis);
        Assert.Null(report.Ai.CoverageReview);
        Assert.Contains(report.Ai.Notices, n => n.Stage == "ai-evidence-analyzer");
        Assert.Contains(report.Ai.Notices, n => n.Stage == "ai-coverage-reviewer");
    }

    [Fact]
    public async Task NoProviderFallbackAndDashboardKeepEvidenceAndAnalysisSeparate()
    {
        using var workspace = new TestWorkspace();
        var plan = workspace.Plan();
        var report = await new ValidationWorkflow(new FakeInspector(plan.Repository), new FakeProcessRunner())
            .RunAsync(plan, null);
        var dashboard = ValidationDashboard.FromReport(report);
        string json = dashboard.ToJson();
        Assert.Contains("\"not-validated\"", json);
        Assert.Contains("\"not-run\"", json);
        Assert.Contains("\"deterministicEvidence\"", json);
        Assert.Contains("\"aiAnalysis\"", json);
        var parsed = ValidationJson.Deserialize<ValidationDashboard>(json);
        Assert.Equal(1, parsed.Summary.NotRun);
        Assert.Equal("vc-build", parsed.Checks[0].SourceId);
        Assert.Equal(["ev-source"], parsed.Checks[0].SourceEvidenceIds);
        Assert.Equal(["guide-build"], parsed.Checks[0].GuidanceIds);
        Assert.Null(parsed.AiAnalysis.EvidenceAnalysis);
    }

    [Fact]
    public void ScorecardDoesNotTreatEmptySkippedOrPartialEvidenceAsValidated()
    {
        using var workspace = new TestWorkspace();
        var plan = workspace.Plan();
        Assert.Equal(OverallStatus.NotValidated, ScorecardBuilder.Build(plan with { Commands = [], Criteria = [] }, []).Status);
        Assert.Equal(OverallStatus.NotValidated, ScorecardBuilder.Build(plan, [new("build", ResultStatus.Skipped, "waived", [])]).Status);
        Assert.Equal(OverallStatus.Validated, ScorecardBuilder.Build(plan, [new("build", ResultStatus.Passed, "measured", ["e1"])]).Status);
        var additional = plan.Criteria[0] with { Key = "unmapped", SourceId = "vc-other" };
        Assert.Equal(OverallStatus.PartiallyValidated, ScorecardBuilder.Build(plan with { Criteria = [.. plan.Criteria, additional] },
            [new("build", ResultStatus.Passed, "measured", ["e1"])]).Status);
    }
}
