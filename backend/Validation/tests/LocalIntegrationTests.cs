using Validation.BuildValidation;
using Validation.Dashboard;
using Xunit;

namespace Validation.Tests;

public sealed class LocalIntegrationTests
{
    [Fact]
    public async Task InspectorVerifiesRealGitRootExactCommitBranchAndCleanTree()
    {
        using var workspace = new TestWorkspace();
        var process = new IsolatedGitProcessRunner(workspace);
        await InitRepository(workspace, process);
        var inspector = new GitRepositoryInspector(process);
        var repository = await inspector.InspectAsync(new(workspace.Repo), default);
        Assert.Equal(40, repository.CommitSha.Length);
        Assert.Equal("validation-test", repository.Branch);
        Assert.Single(repository.Artifacts);
        var verified = await inspector.InspectAsync(new(workspace.Repo, repository.CommitSha, repository.Branch), default);
        Assert.Equal(repository.CommitSha, verified.CommitSha);
        Assert.Equal(repository.Artifacts[0].Sha256, verified.Artifacts[0].Sha256);
        await Assert.ThrowsAsync<InvalidDataException>(() => inspector.InspectAsync(new(workspace.Repo, "abcd"), default));
        await Assert.ThrowsAsync<InvalidDataException>(() => inspector.InspectAsync(new(workspace.Repo, new string('0', 40)), default));
        await Assert.ThrowsAsync<InvalidDataException>(() => inspector.InspectAsync(new(workspace.Repo, Branch: "wrong"), default));
        string child = Path.Combine(workspace.Repo, "child");
        Directory.CreateDirectory(child);
        await Assert.ThrowsAsync<InvalidDataException>(() => inspector.InspectAsync(new(child), default));
        await File.WriteAllTextAsync(Path.Combine(workspace.Repo, "untracked.txt"), "uncommitted");
        await Assert.ThrowsAsync<InvalidDataException>(() => inspector.InspectAsync(new(workspace.Repo), default));
    }

    [Fact]
    public async Task LocalRunnerCapturesExitCodeBothStreamsAndMissingTool()
    {
        using var workspace = new TestWorkspace();
        var runner = new LocalProcessRunner();
        var invocation = OperatingSystem.IsWindows()
            ? Invocation(workspace.Repo, Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe",
                ["/d", "/c", "(echo captured-out) & (echo captured-error 1>&2) & exit /b 7"])
            : Invocation(workspace.Repo, "/bin/sh", ["-c", "printf captured-out; printf captured-error >&2; exit 7"]);
        var result = await runner.RunAsync(invocation, default);
        Assert.Equal(7, result.ExitCode);
        Assert.Contains("captured-out", result.StandardOutput);
        Assert.Contains("captured-error", result.StandardError);
        Assert.True(result.FinishedAt >= result.StartedAt);
        var missing = await runner.RunAsync(Invocation(workspace.Repo, "nonexistent-validation-tool-729104", []), default);
        Assert.NotNull(missing.StartError);
        Assert.Null(missing.ExitCode);
    }

    [Fact]
    public async Task LocalRunnerTerminatesTimedOutProcessTree()
    {
        using var workspace = new TestWorkspace();
        var invocation = OperatingSystem.IsWindows()
            ? Invocation(workspace.Repo, Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe",
                ["/d", "/c", "(echo before-timeout) & ping -n 30 127.0.0.1 > nul"])
            : Invocation(workspace.Repo, "/bin/sh", ["-c", "printf before-timeout; sleep 30"]);
        var result = await new LocalProcessRunner().RunAsync(invocation with { TimeoutSeconds = 1 }, default);
        Assert.True(result.TimedOut);
        Assert.False(result.Cancelled);
        Assert.Contains("before-timeout", result.StandardOutput);
        Assert.True(result.FinishedAt - result.StartedAt < TimeSpan.FromSeconds(15));
    }

    [Fact]
    public async Task CliPersistsReportAndDashboardForApprovedLocalExecution()
    {
        using var workspace = new TestWorkspace();
        var process = new IsolatedGitProcessRunner(workspace);
        await InitRepository(workspace, process);
        var repository = await new GitRepositoryInspector(process).InspectAsync(new(workspace.Repo), default);
        var plan = workspace.Plan(TestWorkspace.Command() with { Executable = "dotnet", Arguments = ["--version"] }) with
        {
            Repository = repository
        };
        string proposal = Path.Combine(workspace.Root, "proposal.json");
        string approval = Path.Combine(workspace.Root, "approval.json");
        string reportFile = Path.Combine(workspace.Root, "report.json");
        string dashboardFile = Path.Combine(workspace.Root, "dashboard.json");
        await File.WriteAllTextAsync(proposal, ValidationJson.Serialize(plan));
        await File.WriteAllTextAsync(approval, ValidationJson.Serialize(TestWorkspace.Approve(plan)));
        var result = await process.RunAsync(Invocation(workspace.Root, "dotnet",
            [typeof(ValidationWorkflow).Assembly.Location, "run", proposal, approval, reportFile, dashboardFile]), default);
        Assert.True(result.ExitCode == 0, result.StandardError + result.StandardOutput);
        var report = ValidationJson.Deserialize<ValidationReport>(await File.ReadAllTextAsync(reportFile));
        var dashboard = ValidationJson.Deserialize<ValidationDashboard>(await File.ReadAllTextAsync(dashboardFile));
        Assert.Equal(OverallStatus.Validated, report.Scorecard.Status);
        Assert.Equal(repository.CommitSha, report.Repository.CommitSha);
        Assert.Equal(1, dashboard.Summary.Passed);
        Assert.Contains(report.Evidence, evidence => evidence.Process?.ExitCode == 0);
        var repeat = await process.RunAsync(Invocation(workspace.Root, "dotnet",
            [typeof(ValidationWorkflow).Assembly.Location, "run", proposal, approval, reportFile, dashboardFile]), default);
        Assert.Equal(2, repeat.ExitCode);
        Assert.Contains("already exist", repeat.StandardError);
        Assert.Equal(report.RunId, ValidationJson.Deserialize<ValidationReport>(await File.ReadAllTextAsync(reportFile)).RunId);
    }

    [Fact]
    public async Task CliPlanningCreatesAnEmptyApprovalAndNeverRunsBuilds()
    {
        using var workspace = new TestWorkspace();
        var process = new IsolatedGitProcessRunner(workspace);
        await InitRepository(workspace, process);
        string migration = Path.Combine(workspace.Root, "migration.json");
        string options = Path.Combine(workspace.Root, "options.json");
        string proposal = Path.Combine(workspace.Root, "proposal.json");
        string approvalFile = Path.Combine(workspace.Root, "approval.json");
        await File.WriteAllTextAsync(migration, ValidationJson.Serialize(TestWorkspace.Migration()));
        await File.WriteAllTextAsync(options, ValidationJson.Serialize(new PlanningOptions(workspace.Evidence)));
        var result = await process.RunAsync(Invocation(workspace.Root, "dotnet",
            [typeof(ValidationWorkflow).Assembly.Location, "plan", migration, workspace.Repo, options, proposal, approvalFile]), default);
        Assert.True(result.ExitCode == 0, result.StandardError + result.StandardOutput);
        var plan = ValidationJson.Deserialize<PreparedValidation>(await File.ReadAllTextAsync(proposal));
        var approval = ValidationJson.Deserialize<PlanApproval>(await File.ReadAllTextAsync(approvalFile));
        Assert.Equal(PlanSafety.Fingerprint(plan), approval.PlanFingerprint);
        Assert.Empty(approval.ApprovedCommandIds);
        Assert.Single(plan.Commands);
        Assert.False(Directory.Exists(Path.Combine(workspace.Repo, "bin")));
    }

    private static ProcessInvocation Invocation(string cwd, string executable, IReadOnlyList<string> arguments) =>
        new(executable, arguments, cwd, RunnerEnvironment.Capture(new Dictionary<string, string>()), 60);

    internal static async Task InitRepository(TestWorkspace workspace, IProcessRunner runner, string objectFormat = "sha1")
    {
        async Task Git(params string[] arguments)
        {
            var result = await runner.RunAsync(Invocation(workspace.Repo, "git", arguments), default);
            Assert.True(result.ExitCode == 0, result.StandardError);
        }
        await Git("init", "--initial-branch=validation-test", $"--object-format={objectFormat}");
        await Git("config", "core.autocrlf", "false");
        await File.WriteAllTextAsync(Path.Combine(workspace.Repo, "App.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>");
        await Git("add", "App.csproj");
        await File.WriteAllTextAsync(Path.Combine(workspace.Repo, "Program.cs"), "class Program { }");
        await Git("add", "Program.cs");
        // Only this disposable fixture receives a commit; never commit the user's worktree.
        await Git("-c", "user.name=Validation Tests", "-c", "user.email=validation@example.invalid",
            "-c", "commit.gpgsign=false", "-c", $"core.hooksPath={workspace.Evidence}", "commit", "-m", "Fixture");
    }
}
