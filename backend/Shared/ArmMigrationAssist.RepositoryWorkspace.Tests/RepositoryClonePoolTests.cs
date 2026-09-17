using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ArmMigrationAssist.RepositoryWorkspace.Tests;

public sealed class RepositoryClonePoolTests : IDisposable
{
    private readonly string _root;

    public RepositoryClonePoolTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "arm-workspace-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch { /* best effort */ }
    }

    [Fact]
    public async Task AcquireAsync_RejectsShortShas()
    {
        using var pool = CreatePool(new FakeGitProcess());
        var key = new RepositoryCloneKey("https://github.com/owner/repo", "abc123", Anonymous: true);
        await Assert.ThrowsAsync<ArgumentException>(() => pool.AcquireAsync(key, CancellationToken.None));
    }

    [Fact]
    public async Task AcquireAsync_RejectsNonHttpsUrls()
    {
        using var pool = CreatePool(new FakeGitProcess());
        var key = new RepositoryCloneKey("http://github.com/owner/repo", new string('a', 40), Anonymous: true);
        await Assert.ThrowsAsync<ArgumentException>(() => pool.AcquireAsync(key, CancellationToken.None));
    }

    [Fact]
    public async Task AcquireAsync_RejectsUrlsWithCredentials()
    {
        using var pool = CreatePool(new FakeGitProcess());
        var key = new RepositoryCloneKey("https://user:token@github.com/owner/repo", new string('a', 40), Anonymous: true);
        await Assert.ThrowsAsync<ArgumentException>(() => pool.AcquireAsync(key, CancellationToken.None));
    }

    [Fact]
    public async Task AcquireAsync_ConcurrentCallersShareMaterialization()
    {
        var fake = new FakeGitProcess();
        using var pool = CreatePool(fake);
        var sha = new string('a', 40);
        var key = new RepositoryCloneKey("https://github.com/owner/repo", sha, Anonymous: true);

        var t1 = pool.AcquireAsync(key, CancellationToken.None);
        var t2 = pool.AcquireAsync(key, CancellationToken.None);
        var results = await Task.WhenAll(t1, t2);

        Assert.Equal(1, fake.CloneCount);
        Assert.Equal(results[0].RootPath, results[1].RootPath);
    }

    [Fact]
    public async Task AcquireAsync_DifferentUrlSameShaMaterializeSeparately()
    {
        var fake = new FakeGitProcess();
        using var pool = CreatePool(fake);
        var sha = new string('a', 40);
        var k1 = new RepositoryCloneKey("https://github.com/owner/repo1", sha, Anonymous: true);
        var k2 = new RepositoryCloneKey("https://github.com/owner/repo2", sha, Anonymous: true);

        _ = await pool.AcquireAsync(k1, CancellationToken.None);
        _ = await pool.AcquireAsync(k2, CancellationToken.None);

        Assert.Equal(2, fake.CloneCount);
    }

    [Fact]
    public async Task AcquireAsync_AnonymousAndAuthenticatedCloneSeparately()
    {
        var fake = new FakeGitProcess();
        using var pool = CreatePool(fake);
        var sha = new string('a', 40);
        var anon = new RepositoryCloneKey("https://github.com/owner/repo", sha, Anonymous: true);
        var auth = new RepositoryCloneKey("https://github.com/owner/repo", sha, Anonymous: false);

        var anonClone = await pool.AcquireAsync(anon, CancellationToken.None);
        var authClone = await pool.AcquireAsync(auth, CancellationToken.None);

        Assert.NotEqual(anonClone.RootPath, authClone.RootPath);
        Assert.Equal(2, fake.CloneCount);
    }

    [Fact]
    public async Task AcquireAsync_PopulatesRootPathWithFiles()
    {
        var fake = new FakeGitProcess();
        using var pool = CreatePool(fake);
        var sha = new string('a', 40);
        var key = new RepositoryCloneKey("https://github.com/owner/repo", sha, Anonymous: true);

        var clone = await pool.AcquireAsync(key, CancellationToken.None);

        Assert.True(Directory.Exists(clone.RootPath));
        Assert.True(File.Exists(Path.Combine(clone.RootPath, "README.md")));
    }

    [Fact]
    public async Task AcquireAsync_EnablesLongPathsForWindowsRepositories()
    {
        var fake = new FakeGitProcess();
        using var pool = CreatePool(fake);
        var key = new RepositoryCloneKey(
            "https://github.com/owner/repo",
            new string('a', 40),
            Anonymous: true);

        _ = await pool.AcquireAsync(key, CancellationToken.None);

        Assert.Contains("core.longpaths=true", fake.CloneArguments);
    }

    [Fact]
    public async Task AcquireAsync_ReusesExistingCloneOnRepeatCall()
    {
        var fake = new FakeGitProcess();
        using var pool = CreatePool(fake);
        var sha = new string('a', 40);
        var key = new RepositoryCloneKey("https://github.com/owner/repo", sha, Anonymous: true);

        var first = await pool.AcquireAsync(key, CancellationToken.None);
        var second = await pool.AcquireAsync(key, CancellationToken.None);

        Assert.Equal(first.RootPath, second.RootPath);
        Assert.Equal(1, fake.CloneCount);
    }

    [Fact]
    public async Task AcquireAsync_RerclonesIfCacheDirectoryWasDeleted()
    {
        var fake = new FakeGitProcess();
        using var pool = CreatePool(fake);
        var sha = new string('a', 40);
        var key = new RepositoryCloneKey("https://github.com/owner/repo", sha, Anonymous: true);

        var first = await pool.AcquireAsync(key, CancellationToken.None);
        Directory.Delete(first.RootPath, recursive: true);
        var second = await pool.AcquireAsync(key, CancellationToken.None);

        Assert.True(Directory.Exists(second.RootPath));
        Assert.Equal(2, fake.CloneCount);
    }

    [Fact]
    public async Task AcquireAsync_ReplacesNonRepositoryCacheDirectory()
    {
        var fake = new FakeGitProcess();
        using var pool = CreatePool(fake);
        var key = new RepositoryCloneKey(
            "https://github.com/owner/repo",
            new string('a', 40),
            Anonymous: true);
        var staleDirectory = Path.Combine(_root, key.DirectorySegment);
        Directory.CreateDirectory(staleDirectory);
        var staleFile = Path.Combine(staleDirectory, "partial-clone.txt");
        await File.WriteAllTextAsync(staleFile, "incomplete");
        File.SetAttributes(staleFile, FileAttributes.ReadOnly);

        var clone = await pool.AcquireAsync(key, CancellationToken.None);

        Assert.Equal(1, fake.CloneCount);
        Assert.False(File.Exists(staleFile));
        Assert.True(File.Exists(Path.Combine(clone.RootPath, "README.md")));
    }

    private RepositoryClonePool CreatePool(IGitProcess gitProcess)
    {
        var options = Options.Create(new RepositoryClonePoolOptions
        {
            RootDirectory = _root,
        });
        return new RepositoryClonePool(
            options,
            gitProcess,
            new StubLifetime(),
            NullLogger<RepositoryClonePool>.Instance);
    }

    // Simulates the pool's expected git invocations without touching the network. Materializes
    // `clone` calls by creating the destination directory + a stub README so the pool's directory
    // existence + reuse checks behave as they would with a real clone.
    private sealed class FakeGitProcess : IGitProcess
    {
        private int _cloneCount;

        public int CloneCount => Volatile.Read(ref _cloneCount);
        public IReadOnlyList<string> CloneArguments { get; private set; } = [];

        public async Task<GitProcessResult> RunAsync(
            string workingDirectory,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            await Task.Yield();
            if (arguments.Count >= 1 && arguments[0] == "clone")
            {
                Interlocked.Increment(ref _cloneCount);
                CloneArguments = arguments.ToArray();
                var destination = arguments[^1];
                Directory.CreateDirectory(destination);
                await File.WriteAllTextAsync(Path.Combine(destination, "README.md"), "fake", cancellationToken);
                return new GitProcessResult(0, "", "");
            }
            if (arguments.Count >= 1 && arguments[0] == "rev-parse")
            {
                return File.Exists(Path.Combine(workingDirectory, "README.md"))
                    ? new GitProcessResult(0, new string('a', 40), "")
                    : new GitProcessResult(128, "", "not a git repository");
            }
            return new GitProcessResult(0, "", "");
        }

        public Task<GitProcessResult> RunAsync(
            string workingDirectory,
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken)
            => RunAsync(workingDirectory, arguments, cancellationToken);
    }

    private sealed class StubLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted { get; } = CancellationToken.None;
        public CancellationToken ApplicationStopping { get; } = CancellationToken.None;
        public CancellationToken ApplicationStopped { get; } = CancellationToken.None;
        public void StopApplication() { }
    }
}
