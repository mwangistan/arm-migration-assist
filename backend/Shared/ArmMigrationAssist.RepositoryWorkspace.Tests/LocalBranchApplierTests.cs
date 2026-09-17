using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ArmMigrationAssist.RepositoryWorkspace.Tests;

// End-to-end integration tests that exercise real `git worktree add`, `git apply`, and
// `git commit` against a locally-initialized parent repo. Skipped if git is unavailable.
public sealed class LocalBranchApplierTests : IDisposable
{
    private readonly string _root;
    private readonly string _parentRepo;
    private readonly RepositoryClone _parent;

    public LocalBranchApplierTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "arm-applier-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _parentRepo = Path.Combine(_root, "parent");
        Directory.CreateDirectory(_parentRepo);

        var git = new GitProcess(Options.Create(new RepositoryClonePoolOptions()));
        // Initialize a real git repo with a single commit so the worktree add + patch apply
        // machinery has a legitimate HEAD to branch off.
        Sync(git, _parentRepo, new[] { "init", "-b", "main" });
        Sync(git, _parentRepo, new[] { "-c", "user.email=t@t", "-c", "user.name=t", "config", "core.autocrlf", "false" });
        File.WriteAllText(Path.Combine(_parentRepo, "README.md"), "hello\n");
        Sync(git, _parentRepo, new[] { "add", "README.md" });
        Sync(git, _parentRepo, new[]
        {
            "-c", "user.email=t@t", "-c", "user.name=t",
            "commit", "-m", "initial",
        });
        var head = SyncOutput(git, _parentRepo, new[] { "rev-parse", "HEAD" });
        var sha = head.Trim();
        _parent = new RepositoryClone(_parentRepo, new RepositoryCloneKey("https://github.com/o/r", sha, Anonymous: true), sha);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch { /* best effort */ }
    }

    [Fact]
    public async Task ApplyAsync_AppliesCleanPatchAndCommits()
    {
        var (applier, worktree) = await CreateHarnessAsync("run1", "arm-migration/run1");

        var patch = @"--- /dev/null
+++ b/.github/workflows/arm64-build.yml
@@ -0,0 +1,2 @@
+name: arm64-build
+on: [push]
";

        var result = await applier.ApplyAsync(
            worktree,
            new[] { new PatchInput("wi-1", patch) },
            "arm-migration: test-plan",
            CancellationToken.None);

        Assert.True(result.CommitCreated);
        Assert.Contains("wi-1", result.AppliedIds);
        Assert.Empty(result.RejectedIds);
        Assert.NotEmpty(result.BranchHeadSha);
        Assert.True(File.Exists(Path.Combine(worktree.Path, ".github", "workflows", "arm64-build.yml")));
    }

    [Fact]
    public async Task ApplyAsync_RejectsInvalidPatchAndStillCompletes()
    {
        var (applier, worktree) = await CreateHarnessAsync("run2", "arm-migration/run2");
        var bogus = "not a diff at all\n";

        var result = await applier.ApplyAsync(
            worktree,
            new[] { new PatchInput("wi-bad", bogus) },
            "arm-migration: test-plan",
            CancellationToken.None);

        Assert.False(result.CommitCreated);
        Assert.Empty(result.AppliedIds);
        Assert.Single(result.RejectedIds);
        Assert.Equal("wi-bad", result.RejectedIds[0].Id);
    }

    [Fact]
    public async Task ApplyAsync_MixedGoodAndBad_AppliesOnlyGood()
    {
        var (applier, worktree) = await CreateHarnessAsync("run3", "arm-migration/run3");
        var good = @"--- /dev/null
+++ b/hello.txt
@@ -0,0 +1,1 @@
+hi
";
        var bad = "malformed\n";

        var result = await applier.ApplyAsync(
            worktree,
            new[] { new PatchInput("good", good), new PatchInput("bad", bad) },
            "arm-migration: test-plan",
            CancellationToken.None);

        Assert.True(result.CommitCreated);
        Assert.Equal(new[] { "good" }, result.AppliedIds);
        Assert.Single(result.RejectedIds);
        Assert.Equal("bad", result.RejectedIds[0].Id);
    }

    private async Task<(LocalBranchApplier Applier, Worktree Worktree)> CreateHarnessAsync(string worktreeId, string branchName)
    {
        var options = Options.Create(new RepositoryClonePoolOptions { RootDirectory = _root });
        var git = new GitProcess(options);
        var lifetime = new StubLifetime();
        var manager = new WorktreeManager(options, git, lifetime, NullLogger<WorktreeManager>.Instance);
        var applier = new LocalBranchApplier(git, NullLogger<LocalBranchApplier>.Instance);
        var worktree = await manager.CreateAsync(_parent, worktreeId, branchName, CancellationToken.None);
        return (applier, worktree);
    }

    private static void Sync(GitProcess git, string cwd, string[] args)
    {
        var result = git.RunAsync(cwd, args, CancellationToken.None).GetAwaiter().GetResult();
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"git {string.Join(' ', args)} exited {result.ExitCode}: {result.StdErr}");
        }
    }

    private static string SyncOutput(GitProcess git, string cwd, string[] args)
    {
        var result = git.RunAsync(cwd, args, CancellationToken.None).GetAwaiter().GetResult();
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"git {string.Join(' ', args)} exited {result.ExitCode}: {result.StdErr}");
        }
        return result.StdOut;
    }

    private sealed class StubLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted { get; } = CancellationToken.None;
        public CancellationToken ApplicationStopping { get; } = CancellationToken.None;
        public CancellationToken ApplicationStopped { get; } = CancellationToken.None;
        public void StopApplication() { }
    }
}
