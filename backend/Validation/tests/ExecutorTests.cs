using Validation.BuildValidation;
using Xunit;

namespace Validation.Tests;

public sealed class ExecutorTests
{
    [Theory]
    [InlineData("missing")]
    [InlineData("empty")]
    [InlineData("mismatch")]
    [InlineData("unknown")]
    public async Task ApprovalFailuresNeverExecuteCommands(string mode)
    {
        using var workspace = new TestWorkspace();
        var plan = workspace.Plan();
        PlanApproval? approval = mode switch
        {
            "missing" => null,
            "empty" => new(PlanSafety.Fingerprint(plan), []),
            "mismatch" => new("old-hash", ["build"]),
            _ => new(PlanSafety.Fingerprint(plan), ["unknown"])
        };
        var runner = new FakeProcessRunner();
        var batch = await new ValidationExecutor(new FakeInspector(plan.Repository), runner).ExecuteAsync(plan, approval);
        Assert.Empty(runner.Calls);
        Assert.All(batch.Results, result => Assert.Equal(ResultStatus.NotRun, result.Status));
        Assert.All(batch.Results, result => Assert.NotEmpty(result.EvidenceIds));
    }

    [Fact]
    public async Task ApprovalBindsArgumentsEnvironmentMappingsAndCommit()
    {
        using var workspace = new TestWorkspace();
        var plan = workspace.Plan();
        var approval = TestWorkspace.Approve(plan);
        var variants = new[]
        {
            plan with { Commands = [plan.Commands[0] with { Arguments = ["different"] }] },
            plan with { Commands = [plan.Commands[0] with { Environment = new Dictionary<string, string> { ["FLAG"] = "changed" } }] },
            plan with { Repository = plan.Repository with { CommitSha = new string('c', 40) } },
            plan with { Criteria = [plan.Criteria[0] with { ExpectedOutcome = "Changed acceptance meaning" }] }
        };
        foreach (var variant in variants)
        {
            var process = new FakeProcessRunner();
            var result = await new ValidationExecutor(new FakeInspector(variant.Repository), process).ExecuteAsync(variant, approval);
            Assert.Empty(process.Calls);
            Assert.Equal(ResultStatus.NotRun, result.Results[0].Status);
        }
    }

    [Fact]
    public async Task PartialApprovalAndExplicitSkipRemainDistinct()
    {
        using var workspace = new TestWorkspace();
        var plan = workspace.Plan(TestWorkspace.Command("first"), TestWorkspace.Command("second"), TestWorkspace.Command("third"));
        var approval = new PlanApproval(PlanSafety.Fingerprint(plan), ["first"], new Dictionary<string, string> { ["third"] = "Out of scope, approved by owner." });
        var runner = new FakeProcessRunner();
        var batch = await new ValidationExecutor(new FakeInspector(plan.Repository), runner).ExecuteAsync(plan, approval);
        Assert.Single(runner.Calls);
        Assert.Equal([ResultStatus.Passed, ResultStatus.NotRun, ResultStatus.Skipped], batch.Results.Select(r => r.Status));
        Assert.Equal(ResultStatus.Inconclusive, ScorecardBuilder.Build(plan, batch.Results).Criteria[0].Status);
    }

    [Theory]
    [InlineData("commit")]
    [InlineData("dirty")]
    [InlineData("artifact")]
    public async Task RepositoryDriftBlocksTheEntireRun(string drift)
    {
        using var workspace = new TestWorkspace();
        var plan = workspace.Plan();
        var inspector = new FakeInspector(plan.Repository);
        if (drift == "dirty") inspector.Error = new InvalidDataException("Repository is dirty.");
        if (drift == "commit") inspector.Context = plan.Repository with { CommitSha = new string('d', 40) };
        if (drift == "artifact") inspector.Context = plan.Repository with { Artifacts = [new("new.csproj", "hash", "<Project />")] };
        var runner = new FakeProcessRunner();
        var batch = await new ValidationExecutor(inspector, runner).ExecuteAsync(plan, TestWorkspace.Approve(plan));
        Assert.Empty(runner.Calls);
        Assert.Equal(ResultStatus.NotRun, batch.Results[0].Status);
        Assert.Contains(batch.Evidence, e => e.Kind == "repository-verification");
    }

    [Theory]
    [InlineData("exit", ResultStatus.Failed)]
    [InlineData("missing", ResultStatus.NotRun)]
    [InlineData("timeout", ResultStatus.Inconclusive)]
    [InlineData("cancel", ResultStatus.Inconclusive)]
    [InlineData("sdk", ResultStatus.Inconclusive)]
    [InlineData("docker", ResultStatus.Inconclusive)]
    [InlineData("capture", ResultStatus.Inconclusive)]
    public async Task FailureAndUnavailableEvidenceCannotPass(string outcome, ResultStatus expected)
    {
        using var workspace = new TestWorkspace();
        var plan = workspace.Plan(TestWorkspace.Command("first"), TestWorkspace.Command("dependent", ["first"]));
        var runner = new FakeProcessRunner
        {
            Execute = _ => outcome switch
            {
                "missing" => FakeProcessRunner.Outcome(null, startError: "Tool unavailable"),
                "timeout" => FakeProcessRunner.Outcome(null, timedOut: true),
                "cancel" => FakeProcessRunner.Outcome(null, cancelled: true),
                "sdk" => FakeProcessRunner.Outcome(1, stderr: "error MSB8020: build tools cannot be found"),
                "docker" => FakeProcessRunner.Outcome(1, stderr: "Cannot connect to the Docker daemon"),
                "capture" => FakeProcessRunner.Outcome() with { OutputIncomplete = true },
                _ => FakeProcessRunner.Outcome(7, "build output", "ARM64 linker failure")
            }
        };
        var batch = await new ValidationExecutor(new FakeInspector(plan.Repository), runner).ExecuteAsync(plan, TestWorkspace.Approve(plan));
        Assert.Single(runner.Calls);
        Assert.Equal(expected, batch.Results[0].Status);
        Assert.Equal(ResultStatus.NotRun, batch.Results[1].Status);
        var evidence = Assert.Single(batch.Evidence, item => item.CommandId == "first");
        Assert.NotNull(evidence.Process);
        Assert.Equal(plan.Commands[0].Arguments, evidence.Invocation!.Arguments);
        Assert.Equal(workspace.Repo, evidence.Invocation.WorkingDirectory);
        Assert.True(evidence.Process.FinishedAt >= evidence.Process.StartedAt);
    }

    [Fact]
    public async Task CrossBuildSuccessDoesNotRunArmTestsOnX64()
    {
        using var workspace = new TestWorkspace();
        var plan = new DeterministicPlanner().Prepare(TestWorkspace.Migration(),
            workspace.Context(("Test.csproj", "<Project><IsTestProject>true</IsTestProject><TargetFramework>net8.0</TargetFramework></Project>")), new(workspace.Evidence));
        var runner = new FakeProcessRunner();
        var batch = await new ValidationExecutor(new FakeInspector(plan.Repository), runner, new("windows", "x64", "test-runner"))
            .ExecuteAsync(plan, TestWorkspace.Approve(plan));
        Assert.Single(runner.Calls);
        Assert.Equal(ResultStatus.Passed, batch.Results[0].Status);
        Assert.Equal(ResultStatus.NotRun, batch.Results[1].Status);
        Assert.Contains("Windows ARM64 runner", batch.Results[1].Reason);
    }

    [Theory]
    [InlineData("missing", ResultStatus.Inconclusive)]
    [InlineData("empty", ResultStatus.Inconclusive)]
    [InlineData("skipped", ResultStatus.Inconclusive)]
    [InlineData("failure", ResultStatus.Failed)]
    [InlineData("malformed", ResultStatus.Inconclusive)]
    [InlineData("pass", ResultStatus.Passed)]
    [InlineData("stale", ResultStatus.Inconclusive)]
    [InlineData("contradictory", ResultStatus.Failed)]
    public async Task DotNetTestNeedsFreshPositiveTrxEvidence(string proof, ResultStatus expected)
    {
        using var workspace = new TestWorkspace();
        string path = Path.Combine(workspace.Evidence, "results.trx");
        var command = TestWorkspace.Command() with
        {
            Kind = CommandKind.DotNetTest, Surface = ExecutionSurface.WindowsArm64Runtime,
            Proof = ProofKind.Trx, ProofPath = path, Arguments = ["test", "--framework", "net8.0"]
        };
        var plan = workspace.Plan(command);
        if (proof == "stale")
        {
            Directory.CreateDirectory(workspace.Evidence);
            File.WriteAllText(path, "<stale />");
        }
        var runner = new FakeProcessRunner
        {
            Execute = _ =>
            {
                if (proof != "missing")
                    File.WriteAllText(path, proof switch
                    {
                        "malformed" => "<broken>",
                        "empty" => Trx(0, 0, 0, 0),
                        "skipped" => Trx(2, 1, 1, 0),
                        "failure" => Trx(2, 2, 1, 1),
                        "contradictory" => Trx(2, 2, 2, 0).Replace("<ResultSummary>", "<ResultSummary outcome=\"Failed\">"),
                        _ => Trx(2, 2, 2, 0)
                    });
                return FakeProcessRunner.Outcome();
            }
        };
        var result = await new ValidationExecutor(new FakeInspector(plan.Repository), runner, new("windows", "arm64", "test-runner"))
            .ExecuteAsync(plan, TestWorkspace.Approve(plan));
        Assert.Equal(expected, result.Results[0].Status);
        if (proof == "stale") Assert.Empty(runner.Calls);
        if (proof is not ("missing" or "stale"))
            Assert.Single(result.Evidence.Single(e => e.CommandId == command.Id && e.Kind == "command-execution").Artifacts);
    }

    [Theory]
    [InlineData("linux/amd64", ResultStatus.Failed)]
    [InlineData("linux/arm64", ResultStatus.Passed)]
    public async Task ContainerBuildRequiresArchitectureMetadata(string architecture, ResultStatus expected)
    {
        using var workspace = new TestWorkspace();
        var plan = new DeterministicPlanner().Prepare(TestWorkspace.Migration(),
            workspace.Context(("Dockerfile", "FROM example")), new(workspace.Evidence));
        var runner = new FakeProcessRunner { Execute = _ => FakeProcessRunner.Outcome(stdout: architecture) };
        var result = await new ValidationExecutor(new FakeInspector(plan.Repository), runner)
            .ExecuteAsync(plan, TestWorkspace.Approve(plan));
        var score = ScorecardBuilder.Build(plan, result.Results);
        Assert.Equal(expected, score.Criteria.Single(c => c.Criterion.SourceId.StartsWith("container-build")).Status);
        Assert.Contains(ScorecardBuilder.Coverage(plan, score, result.Results), g => g.Id == "container-scope");
    }

    [Fact]
    public async Task NativeSmokeRejectsX64ExecutableEvenOnArm64Host()
    {
        using var workspace = new TestWorkspace();
        string executable = Path.Combine(workspace.Repo, "app.exe");
        using (var writer = new BinaryWriter(File.Create(executable)))
        {
            writer.Write((ushort)0x5a4d);
            writer.BaseStream.Position = 0x3c;
            writer.Write(64);
            writer.Write((uint)0x4550);
            writer.Write((ushort)0x8664);
        }
        var plan = workspace.Plan(TestWorkspace.Command() with
        {
            Kind = CommandKind.NativeSmoke, Surface = ExecutionSurface.WindowsArm64Runtime, Executable = executable
        });
        var runner = new FakeProcessRunner();
        var batch = await new ValidationExecutor(new FakeInspector(plan.Repository), runner, new("windows", "arm64", "test-runner"))
            .ExecuteAsync(plan, TestWorkspace.Approve(plan));
        Assert.Empty(runner.Calls);
        Assert.Equal(ResultStatus.Inconclusive, batch.Results[0].Status);
        Assert.Contains("not ARM64", batch.Results[0].Reason);
    }

    [Fact]
    public async Task CapturesEffectiveEnvironmentButRedactsCredentialOverrides()
    {
        using var workspace = new TestWorkspace();
        var plan = workspace.Plan(TestWorkspace.Command() with
        {
            Environment = new Dictionary<string, string> { ["BUILD_MODE"] = "Release", ["ACCESS_TOKEN"] = "test-only-value" }
        });
        var runner = new FakeProcessRunner();
        var batch = await new ValidationExecutor(new FakeInspector(plan.Repository), runner).ExecuteAsync(plan, TestWorkspace.Approve(plan));
        Assert.Equal("test-only-value", runner.Calls[0].Environment["ACCESS_TOKEN"]);
        var captured = batch.Evidence.Single(e => e.CommandId == "build").Invocation!.Environment;
        Assert.Equal("[REDACTED]", captured["ACCESS_TOKEN"]);
        Assert.Equal("Release", captured["BUILD_MODE"]);
        Assert.DoesNotContain("test-only-value", ValidationJson.Serialize(batch));
    }

    [Fact]
    public async Task CancellationBeforeStartPreservesNotRunEvidence()
    {
        using var workspace = new TestWorkspace();
        var plan = workspace.Plan();
        var runner = new FakeProcessRunner();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var batch = await new ValidationExecutor(new FakeInspector(plan.Repository), runner)
            .ExecuteAsync(plan, TestWorkspace.Approve(plan), cancellation.Token);
        Assert.Empty(runner.Calls);
        Assert.Equal(ResultStatus.NotRun, batch.Results[0].Status);
    }

    [Fact]
    public async Task RepositoryChangesDuringCommandsDowngradePassingResultsToInconclusive()
    {
        using var workspace = new TestWorkspace();
        var plan = workspace.Plan();
        var inspector = new FakeInspector(plan.Repository);
        var runner = new FakeProcessRunner
        {
            Execute = _ =>
            {
                inspector.Error = new InvalidDataException("Tracked source was modified by the build.");
                return FakeProcessRunner.Outcome();
            }
        };
        var batch = await new ValidationExecutor(inspector, runner).ExecuteAsync(plan, TestWorkspace.Approve(plan));
        Assert.Equal(ResultStatus.Inconclusive, batch.Results[0].Status);
        Assert.Contains(batch.Evidence, item => item.Kind == "repository-verification-after-run" && item.Summary.Contains("modified"));
    }

    [Fact]
    public async Task NativeArm64PeSmokeExecutesOnlyAfterSuccessfulBuild()
    {
        using var workspace = new TestWorkspace();
        var plan = new DeterministicPlanner().Prepare(TestWorkspace.Migration(), workspace.Context(
            ("App.vcxproj", "<Project><ProjectConfiguration Include=\"Release|ARM64\" /></Project>")),
            new(workspace.Evidence, NativeSmokeChecks:
                [new("App.vcxproj", "app.exe", ["--smoke"], ["validation:vc-function"])]));
        string executable = Path.Combine(workspace.Repo, "app.exe");
        var runner = new FakeProcessRunner
        {
            Execute = invocation =>
            {
                if (invocation.Executable == "msbuild")
                {
                    using var writer = new BinaryWriter(File.Create(executable));
                    writer.Write((ushort)0x5a4d);
                    writer.BaseStream.Position = 0x3c;
                    writer.Write(64);
                    writer.Write((uint)0x4550);
                    writer.Write((ushort)0xaa64);
                }
                else Assert.Equal(executable, invocation.Executable);
                return FakeProcessRunner.Outcome();
            }
        };
        var batch = await new ValidationExecutor(new FakeInspector(plan.Repository), runner, new("windows", "arm64", "test-runner"))
            .ExecuteAsync(plan, TestWorkspace.Approve(plan));
        Assert.Equal(2, runner.Calls.Count);
        Assert.All(batch.Results, result => Assert.Equal(ResultStatus.Passed, result.Status));
        Assert.Equal(ResultStatus.Passed, ScorecardBuilder.Build(plan, batch.Results).Criteria
            .Single(result => result.Criterion.Key == "discovered:windows-arm64-runtime").Status);
    }

    [Fact]
    public async Task ExplicitContainerSmokeExecutesAfterVerifiedArm64Image()
    {
        using var workspace = new TestWorkspace();
        var plan = new DeterministicPlanner().Prepare(TestWorkspace.Migration(), workspace.Context(("Dockerfile", "FROM example")),
            new(workspace.Evidence, ContainerSmokeChecks: [new("Dockerfile", ["app", "--smoke"], ["validation:vc-function"])]));
        var runner = new FakeProcessRunner
        {
            Execute = invocation => FakeProcessRunner.Outcome(stdout: invocation.Arguments[0] == "image" ? "linux/arm64" : "ok")
        };
        var batch = await new ValidationExecutor(new FakeInspector(plan.Repository), runner).ExecuteAsync(plan, TestWorkspace.Approve(plan));
        Assert.Equal(["build", "image", "run"], runner.Calls.Select(call => call.Arguments[0]));
        Assert.All(batch.Results, result => Assert.Equal(ResultStatus.Passed, result.Status));
    }

    [Theory]
    [InlineData("pass", ResultStatus.Passed)]
    [InlineData("failed", ResultStatus.Failed)]
    [InlineData("missing", ResultStatus.Inconclusive)]
    [InlineData("skipped", ResultStatus.Inconclusive)]
    public async Task LaterFrameworkProofCannotHideEarlierFrameworkFailureOrGap(string first, ResultStatus expected)
    {
        using var workspace = new TestWorkspace();
        var plan = new DeterministicPlanner().Prepare(TestWorkspace.Migration(), workspace.Context(
            ("Tests.csproj", "<Project><IsTestProject>true</IsTestProject><TargetFrameworks>net8.0;net9.0</TargetFrameworks></Project>")),
            new(workspace.Evidence));
        var tests = plan.Commands.Where(command => command.Kind == CommandKind.DotNetTest).ToArray();
        var runner = new FakeProcessRunner
        {
            Execute = invocation =>
            {
                if (invocation.Arguments[0] == "test")
                {
                    bool earlier = invocation.Arguments.Contains("net8.0");
                    if (!earlier || first != "missing")
                    {
                        string directory = invocation.Arguments[invocation.Arguments.ToList().IndexOf("--results-directory") + 1];
                        File.WriteAllText(Path.Combine(directory, "results.trx"),
                            earlier && first == "failed" ? Trx(2, 2, 1, 1) :
                            earlier && first == "skipped" ? Trx(2, 1, 1, 0) : Trx(2, 2, 2, 0));
                    }
                }
                return FakeProcessRunner.Outcome();
            }
        };
        var batch = await new ValidationExecutor(new FakeInspector(plan.Repository), runner, new("windows", "arm64", "runner"))
            .ExecuteAsync(plan, TestWorkspace.Approve(plan));
        Assert.Equal(3, runner.Calls.Count);
        Assert.Equal(expected, batch.Results.Single(result => result.CommandId == tests[0].Id).Status);
        Assert.Equal(ResultStatus.Passed, batch.Results.Single(result => result.CommandId == tests[1].Id).Status);
        string projectKey = Assert.Single(tests[0].CriterionKeys.Intersect(tests[1].CriterionKeys),
            key => key != "discovered:windows-arm64-runtime");
        var score = ScorecardBuilder.Build(plan, batch.Results);
        Assert.Equal(expected, score.Criteria.Single(result => result.Criterion.Key == projectKey).Status);
        var proofs = batch.Evidence.SelectMany(evidence => evidence.Artifacts).ToArray();
        Assert.Equal(first == "missing" ? 1 : 2, proofs.Length);
        Assert.Equal(proofs.Length, proofs.Select(proof => proof.Path).Distinct().Count());
    }

    [Theory]
    [InlineData(7, ResultStatus.Failed)]
    [InlineData(0, ResultStatus.Inconclusive)]
    [InlineData(null, ResultStatus.NotRun)]
    public async Task ProofReadOrHashErrorsPreserveEstablishedOutcomeAndRecordDiagnostics(int? exitCode, ResultStatus expected)
    {
        using var workspace = new TestWorkspace();
        string path = Path.Combine(workspace.Evidence, "results.trx");
        var plan = workspace.Plan(TestWorkspace.Command() with
        {
            Kind = CommandKind.DotNetTest, Surface = ExecutionSurface.WindowsArm64Runtime,
            Proof = ProofKind.Trx, ProofPath = path, Arguments = ["test", "--framework", "net8.0"]
        });
        FileStream? lockedProof = null;
        try
        {
            var runner = new FakeProcessRunner
            {
                Execute = _ =>
                {
                    File.WriteAllText(path, Trx(2, 2, 2, 0));
                    lockedProof = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                    return FakeProcessRunner.Outcome(exitCode, startError: exitCode is null ? "Cannot start tool" : null);
                }
            };
            var batch = await new ValidationExecutor(new FakeInspector(plan.Repository), runner, new("windows", "arm64", "runner"))
                .ExecuteAsync(plan, TestWorkspace.Approve(plan));
            var result = Assert.Single(batch.Results);
            Assert.Equal(expected, result.Status);
            if (exitCode == 7) Assert.Contains("Command exited with code 7", result.Reason);
            if (exitCode is null) Assert.Contains("Tool could not start", result.Reason);
            var diagnostics = batch.Evidence.Where(evidence => evidence.Kind == "evidence-diagnostic").ToArray();
            Assert.NotEmpty(diagnostics);
            Assert.All(diagnostics, diagnostic => Assert.Contains(diagnostic.Id, result.EvidenceIds));
            var execution = Assert.Single(batch.Evidence, evidence => evidence.Kind == "command-execution");
            Assert.Equal(exitCode, execution.Process!.ExitCode);
            Assert.Contains("Evidence unavailable", execution.Summary);
            Assert.Empty(execution.Artifacts);
        }
        finally { lockedProof?.Dispose(); }
    }

    private static string Trx(int total, int executed, int passed, int failed) =>
        $"<TestRun xmlns=\"http://microsoft.com/schemas/VisualStudio/TeamTest/2010\"><ResultSummary><Counters total=\"{total}\" executed=\"{executed}\" passed=\"{passed}\" failed=\"{failed}\" /></ResultSummary></TestRun>";
}
