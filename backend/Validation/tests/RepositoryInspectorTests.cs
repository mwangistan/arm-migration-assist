using Validation.BuildValidation;
using Xunit;

namespace Validation.Tests;

public sealed class RepositoryInspectorTests
{
    [Theory]
    [InlineData("clean", "local", "review")]
    [InlineData("smudge", "local", "review")]
    [InlineData("process", "local", "review")]
    [InlineData("clean", "include", "review")]
    [InlineData("process", "global", "review")]
    [InlineData("clean", "local", "")]
    public async Task ExecutableFiltersAreRejectedBeforeIndexOrWorktreeInspection(string filter, string scope, string driver)
    {
        using var workspace = new TestWorkspace();
        var runner = new IsolatedGitProcessRunner(workspace);
        await LocalIntegrationTests.InitRepository(workspace, runner);
        await File.WriteAllTextAsync(Path.Combine(workspace.Repo, ".gitattributes"), "*.cs filter=review");
        await Git(workspace, runner, "add", ".gitattributes");
        await Commit(workspace, runner);
        string key = $"filter.{driver}.{filter}";
        string command = "echo invoked > ../filter-ran; exit 1";
        if (scope == "include")
        {
            string included = Path.Combine(workspace.Root, "included-config");
            await Git(workspace, runner, "config", "--file", included, key, command);
            await Git(workspace, runner, "config", "include.path", included);
        }
        else if (scope == "global")
            await Git(workspace, runner, "config", "--global", key, command);
        else
            await Git(workspace, runner, "config", key, command);
        await File.WriteAllTextAsync(Path.Combine(workspace.Repo, "Program.cs"), "class Changed { }");
        int start = runner.Calls.Count;

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new GitRepositoryInspector(runner).InspectAsync(new(workspace.Repo), default));

        Assert.Contains("Executable Git filter", error.Message);
        Assert.False(File.Exists(Path.Combine(workspace.Root, "filter-ran")));
        Assert.DoesNotContain(runner.Calls.Skip(start), call =>
            call.Arguments.Contains("status") || call.Arguments.Contains("ls-files"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StagedAndCommittedSubmodulesAreRejectedWithoutRecursing(bool committed)
    {
        using var workspace = new TestWorkspace();
        var runner = new IsolatedGitProcessRunner(workspace);
        await LocalIntegrationTests.InitRepository(workspace, runner);
        string sha = await Git(workspace, runner, "rev-parse", "HEAD");
        await Git(workspace, runner, "update-index", "--add", "--cacheinfo", $"160000,{sha},nested");
        if (committed) await Commit(workspace, runner);
        Directory.CreateDirectory(Path.Combine(workspace.Repo, "nested"));
        int start = runner.Calls.Count;

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new GitRepositoryInspector(runner).InspectAsync(new(workspace.Repo), default));

        Assert.Contains("Submodules", error.Message);
        Assert.DoesNotContain(runner.Calls.Skip(start), call =>
            call.Arguments.Contains("status") || call.Arguments.Contains("--others"));
    }

    [Theory]
    [InlineData("--assume-unchanged", false)]
    [InlineData("--assume-unchanged", true)]
    [InlineData("--skip-worktree", false)]
    [InlineData("--skip-worktree", true)]
    public async Task UnsupportedIndexFlagsCannotHideTrackedSources(string flag, bool modified)
    {
        using var workspace = new TestWorkspace();
        var runner = new IsolatedGitProcessRunner(workspace);
        await LocalIntegrationTests.InitRepository(workspace, runner);
        await Git(workspace, runner, "update-index", flag, "Program.cs");
        if (modified) await File.WriteAllTextAsync(Path.Combine(workspace.Repo, "Program.cs"), "class Changed { }");
        Assert.Equal("", await Git(workspace, runner, "status", "--porcelain"));

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new GitRepositoryInspector(runner).InspectAsync(new(workspace.Repo), default));

        Assert.Contains("Unsupported index flags", error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EveryTrackedSourceMustMatchCommitNotJustDiscoveredProjects(bool staged)
    {
        using var workspace = new TestWorkspace();
        var runner = new IsolatedGitProcessRunner(workspace);
        await LocalIntegrationTests.InitRepository(workspace, runner);
        var inspector = new GitRepositoryInspector(runner);
        var context = await inspector.InspectAsync(new(workspace.Repo), default);
        string source = Path.Combine(workspace.Repo, "Program.cs");
        DateTime timestamp = File.GetLastWriteTimeUtc(source);
        await File.WriteAllTextAsync(source, "class Changed { }");
        File.SetLastWriteTimeUtc(source, timestamp);
        if (staged) await Git(workspace, runner, "add", "Program.cs");

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            inspector.InspectAsync(new(workspace.Repo, context.CommitSha), default));

        Assert.True(
            error.Message.Contains("Index does not match", StringComparison.Ordinal) ||
            error.Message.Contains("Tracked file bytes do not match", StringComparison.Ordinal),
            $"Unexpected provenance rejection: {error.Message}");
        Assert.Contains("Program.cs", error.Message);
    }

    [Fact]
    public async Task ApprovedCommandCannotAttributeModifiedNonProjectSourcesToPinnedCommit()
    {
        using var workspace = new TestWorkspace();
        var git = new IsolatedGitProcessRunner(workspace);
        await LocalIntegrationTests.InitRepository(workspace, git);
        var inspector = new GitRepositoryInspector(git);
        var context = await inspector.InspectAsync(new(workspace.Repo), default);
        var plan = workspace.Plan() with { Repository = context };
        var process = new FakeProcessRunner
        {
            Execute = _ =>
            {
                File.WriteAllText(Path.Combine(workspace.Repo, "Program.cs"), "class Changed { }");
                return FakeProcessRunner.Outcome();
            }
        };
        var batch = await new ValidationExecutor(inspector, process).ExecuteAsync(plan, TestWorkspace.Approve(plan));
        Assert.Equal(ResultStatus.Inconclusive, Assert.Single(batch.Results).Status);
        Assert.Contains(batch.Evidence, evidence => evidence.Kind == "repository-verification-after-run" &&
            evidence.Summary.Contains("Program.cs"));
    }

    [Fact]
    public async Task RawBlobVerificationSupportsSha256RepositoriesWithoutStatus()
    {
        using var workspace = new TestWorkspace();
        var runner = new IsolatedGitProcessRunner(workspace);
        await LocalIntegrationTests.InitRepository(workspace, runner, "sha256");
        int start = runner.Calls.Count;
        var repository = await new GitRepositoryInspector(runner).InspectAsync(new(workspace.Repo), default);
        Assert.Equal(64, repository.CommitSha.Length);
        Assert.Single(repository.Artifacts);
        Assert.DoesNotContain(runner.Calls.Skip(start), call => call.Arguments.Contains("status"));
        Assert.All(runner.Calls.Skip(start), call => Assert.Contains("core.fsmonitor=false", call.Arguments));
    }

    [Fact]
    public async Task RawBlobVerificationDoesNotNormalizeCheckoutLineEndings()
    {
        using var workspace = new TestWorkspace();
        var runner = new IsolatedGitProcessRunner(workspace);
        await LocalIntegrationTests.InitRepository(workspace, runner);
        await File.WriteAllTextAsync(Path.Combine(workspace.Repo, "Program.cs"), "class Program { }\n");
        await File.WriteAllTextAsync(Path.Combine(workspace.Repo, ".gitattributes"), "Program.cs text eol=crlf\n");
        await Git(workspace, runner, "add", "Program.cs", ".gitattributes");
        await Commit(workspace, runner);
        File.Delete(Path.Combine(workspace.Repo, "Program.cs"));
        await Git(workspace, runner, "checkout-index", "--force", "--", "Program.cs");
        Assert.EndsWith("\r\n", await File.ReadAllTextAsync(Path.Combine(workspace.Repo, "Program.cs")));
        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new GitRepositoryInspector(runner).InspectAsync(new(workspace.Repo), default));
        Assert.Contains("Tracked file bytes", error.Message);
    }

    private static async Task<string> Git(TestWorkspace workspace, IProcessRunner runner, params string[] arguments)
    {
        var result = await runner.RunAsync(new("git", arguments, workspace.Repo,
            RunnerEnvironment.Capture(new Dictionary<string, string>()), 30), default);
        Assert.True(result.ExitCode == 0, result.StandardError);
        return result.StandardOutput.TrimEnd('\r', '\n');
    }

    private static Task<string> Commit(TestWorkspace workspace, IProcessRunner runner) =>
        Git(workspace, runner, "-c", "user.name=Validation Tests", "-c", "user.email=validation@example.invalid",
            "-c", "commit.gpgsign=false", "-c", $"core.hooksPath={workspace.Evidence}", "commit", "-m", "Fixture");
}
