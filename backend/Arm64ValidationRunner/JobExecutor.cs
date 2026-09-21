using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Arm64ValidationRunner;

// Runs the clone → apply → build → test pipeline for a single job. Fire-and-forget
// entry point kicked off from the HTTP layer; results land in JobStore for polling.
public sealed class JobExecutor
{
    private static readonly TimeSpan GitTimeout = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan BuildTimeout = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan TestTimeout = TimeSpan.FromMinutes(20);

    private readonly RunnerOptions _options;
    private readonly ILogger<JobExecutor> _logger;

    public JobExecutor(RunnerOptions options, ILogger<JobExecutor> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task ExecuteAsync(JobRecord job, RunRequest request, CancellationToken cancellationToken)
    {
        job.Status = "running";
        job.StartedAt = DateTimeOffset.UtcNow;

        var totalSw = Stopwatch.StartNew();
        var scratch = Path.Combine(_options.WorkRoot, "job-" + job.JobId);
        var repoDir = Path.Combine(scratch, "repo");
        Directory.CreateDirectory(scratch);

        try
        {
            ValidateRequest(request);

            var progress = new ProgressTracker(job, new[]
            {
                ("clone", "Clone repository"),
                ("checkout", "Fetch & checkout base commit"),
                ("patches", "Apply patches"),
                ("detect", "Detect toolchain"),
                ("build", "Build (arm64)"),
                ("tests", "Run tests"),
            });

            // 1. Clone (shallow) then fetch + detach onto the base commit.
            // Only pin a branch when one is supplied; otherwise clone the remote's
            // default branch. "HEAD" is not a valid --branch value and makes
            // `git clone --branch HEAD` fail with "Remote branch HEAD not found".
            progress.Start("clone");
            var cloneArgs = new List<string>
            {
                "clone", "--filter=blob:none", "--no-tags",
                "--depth", "1",
            };
            if (!string.IsNullOrWhiteSpace(request.Branch))
            {
                cloneArgs.Add("--branch");
                cloneArgs.Add(request.Branch!);
                cloneArgs.Add("--single-branch");
            }
            cloneArgs.Add(request.SourceUrl);
            cloneArgs.Add("repo");

            var clone = await RunGitAsync(scratch, cloneArgs, cancellationToken);
            if (clone.ExitCode != 0 || clone.TimedOut) progress.Fail("clone", Trim(clone.Stderr));
            EnsureOk(clone, "git clone");
            progress.Succeed("clone");

            progress.Start("checkout");
            var fetch = await RunGitAsync(repoDir, new[] { "fetch", "--depth", "1", "origin", request.BaseCommitSha }, cancellationToken);
            if (fetch.ExitCode != 0 || fetch.TimedOut) progress.Fail("checkout", Trim(fetch.Stderr));
            EnsureOk(fetch, "git fetch");
            var checkout = await RunGitAsync(repoDir, new[] { "checkout", "--detach", request.BaseCommitSha }, cancellationToken);
            if (checkout.ExitCode != 0 || checkout.TimedOut) progress.Fail("checkout", Trim(checkout.Stderr));
            EnsureOk(checkout, "git checkout");
            progress.Succeed("checkout", request.BaseCommitSha[..Math.Min(12, request.BaseCommitSha.Length)]);

            // 2. Apply each patch — strict first, 3-way fallback for context drift.
            progress.Start("patches", $"0/{request.Patches.Count} applied");
            var applied = new List<string>();
            var rejected = new List<PatchRejection>();
            var patchDir = Path.Combine(scratch, "patches");
            Directory.CreateDirectory(patchDir);

            foreach (var patch in request.Patches)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var patchFile = Path.Combine(patchDir, Sanitize(patch.Id) + ".patch");
                await File.WriteAllTextAsync(patchFile, patch.Diff, cancellationToken);

                var check = await RunGitAsync(repoDir, new[] { "apply", "--check", patchFile }, cancellationToken);
                if (check.ExitCode == 0)
                {
                    var apply = await RunGitAsync(repoDir, new[] { "apply", patchFile }, cancellationToken);
                    if (apply.ExitCode == 0) { applied.Add(patch.Id); }
                    else { rejected.Add(new PatchRejection(patch.Id, $"git apply failed: {Trim(apply.Stderr)}")); }
                }
                else
                {
                    var threeWay = await RunGitAsync(repoDir, new[] { "apply", "--3way", patchFile }, cancellationToken);
                    if (threeWay.ExitCode == 0) { applied.Add(patch.Id); }
                    else { rejected.Add(new PatchRejection(patch.Id, $"3-way apply failed: {Trim(threeWay.Stderr)}")); }
                }
                progress.Detail("patches", $"{applied.Count}/{request.Patches.Count} applied");
            }
            progress.Succeed("patches", $"{applied.Count}/{request.Patches.Count} applied, {rejected.Count} rejected");

            // 3. Commit whatever landed so subsequent tools see a clean tree.
            string resolvedSha = request.BaseCommitSha;
            if (applied.Count > 0)
            {
                var addAll = await RunGitAsync(repoDir, new[] { "add", "-A" }, cancellationToken);
                EnsureOk(addAll, "git add -A");
                var commit = await RunGitAsync(repoDir, new[]
                {
                    "-c", "user.name=arm-migration-runner",
                    "-c", "user.email=arm-migration-runner@localhost",
                    "commit", "-m", $"arm-migration: {job.JobId}",
                }, cancellationToken);
                if (commit.ExitCode == 0)
                {
                    var revParse = await RunGitAsync(repoDir, new[] { "rev-parse", "HEAD" }, cancellationToken);
                    if (revParse.ExitCode == 0) resolvedSha = revParse.Stdout.Trim();
                }
            }

            var patchReport = new PatchApplicationReport(applied, rejected);

            // 4. Detect toolchain and run build + tests.
            progress.Start("detect");
            var detected = ToolchainDetector.Detect(repoDir, request.ProjectHints);
            _logger.LogInformation("Job {JobId} detected toolchain: {Kind}", job.JobId, detected.Kind);
            progress.Succeed("detect", detected.Kind.ToString());

            StepOutcome? build = null;
            StepOutcome? tests = null;

            switch (detected.Kind)
            {
                case ToolchainKind.Dotnet:
                    progress.Start("build", "dotnet build --arch arm64");
                    build = await RunDotnetBuildAsync(repoDir, detected, cancellationToken);
                    MarkBuild(progress, build);
                    if (build?.Succeeded == true)
                    {
                        progress.Start("tests", "dotnet test");
                        tests = await RunDotnetTestAsync(repoDir, detected, cancellationToken);
                        MarkTests(progress, tests);
                    }
                    else { progress.Skip("tests", "build failed"); }
                    break;
                case ToolchainKind.Python:
                    progress.Start("build", "pip install");
                    build = await RunPythonInstallAsync(repoDir, detected, cancellationToken);
                    MarkBuild(progress, build);
                    if (build?.Succeeded == true)
                    {
                        progress.Start("tests", "import smoke test");
                        tests = await RunPythonImportSmokeAsync(repoDir, detected, cancellationToken);
                        MarkTests(progress, tests);
                    }
                    else { progress.Skip("tests", "install failed"); }
                    break;
                case ToolchainKind.Node:
                    progress.Start("build", "npm install");
                    build = await RunNodeInstallAsync(repoDir, detected, cancellationToken);
                    MarkBuild(progress, build);
                    if (build?.Succeeded == true)
                    {
                        progress.Start("tests", "npm test");
                        tests = await RunNodeTestAsync(repoDir, detected, cancellationToken);
                        MarkTests(progress, tests);
                    }
                    else { progress.Skip("tests", "install failed"); }
                    break;
                default:
                    build = new StepOutcome("detect", "n/a", repoDir, 0, false, 0, "", "no supported toolchain detected");
                    progress.Skip("build", "no supported toolchain detected");
                    progress.Skip("tests", "no supported toolchain detected");
                    break;
            }

            totalSw.Stop();

            var summary = BuildSummary(patchReport, build, tests, detected.Kind);
            job.Scorecard = new Arm64Scorecard(
                Hardware: HardwareProbe.Snapshot(_options.VmSku, _options.Region),
                SourceCommitSha: request.BaseCommitSha,
                ResolvedCommitSha: resolvedSha,
                PatchApplication: patchReport,
                Build: build,
                Tests: tests,
                WallClockSeconds: totalSw.Elapsed.TotalSeconds,
                Summary: summary);
            progress.Complete();
            job.Status = "completed";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            job.Status = "cancelled";
            job.Error = "Cancelled";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job {JobId} failed", job.JobId);
            job.Status = "failed";
            job.Error = ex.Message;
        }
        finally
        {
            job.FinishedAt = DateTimeOffset.UtcNow;
            TryDeleteDirectory(scratch);
        }
    }

    private static void ValidateRequest(RunRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SourceUrl)) throw new InvalidDataException("sourceUrl is required.");
        if (!Uri.TryCreate(request.SourceUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException("sourceUrl must be an absolute https:// URL.");
        if (!uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) &&
            !uri.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("sourceUrl must be a github.com URL.");
        if (string.IsNullOrWhiteSpace(request.BaseCommitSha) ||
            !Regex.IsMatch(request.BaseCommitSha, @"\A[0-9a-fA-F]{7,64}\z"))
            throw new InvalidDataException("baseCommitSha must be a 7-64 char hex string.");
        if (request.Patches is null || request.Patches.Count == 0)
            throw new InvalidDataException("At least one patch is required.");
    }

    private async Task<StepOutcome> RunDotnetBuildAsync(string repoDir, DetectedToolchain d, CancellationToken ct)
    {
        var target = d.PrimaryEntry ?? repoDir;
        var args = new List<string> { "build", target, "-c", "Release", "--arch", "arm64", "-v", "minimal" };
        var r = await ProcessRunner.RunAsync("dotnet", args, repoDir, BuildTimeout, cancellationToken: ct);
        return new StepOutcome("dotnet", "dotnet " + string.Join(' ', args), repoDir, r.ExitCode,
            r.ExitCode == 0 && !r.TimedOut, r.Duration.TotalSeconds, r.Stdout, r.Stderr);
    }

    private async Task<StepOutcome> RunDotnetTestAsync(string repoDir, DetectedToolchain d, CancellationToken ct)
    {
        var target = d.PrimaryEntry ?? repoDir;
        var args = new List<string> { "test", target, "-c", "Release", "--no-build", "--arch", "arm64", "--logger", "console;verbosity=minimal" };
        var r = await ProcessRunner.RunAsync("dotnet", args, repoDir, TestTimeout, cancellationToken: ct);
        return new StepOutcome("dotnet", "dotnet " + string.Join(' ', args), repoDir, r.ExitCode,
            r.ExitCode == 0 && !r.TimedOut, r.Duration.TotalSeconds, r.Stdout, r.Stderr);
    }

    private async Task<StepOutcome> RunPythonInstallAsync(string repoDir, DetectedToolchain d, CancellationToken ct)
    {
        var venv = Path.Combine(repoDir, ".arm-venv");
        var mkv = await ProcessRunner.RunAsync("python3", new[] { "-m", "venv", venv }, repoDir, BuildTimeout, cancellationToken: ct);
        if (mkv.ExitCode != 0)
        {
            return new StepOutcome("python", "python3 -m venv .arm-venv", repoDir, mkv.ExitCode, false, mkv.Duration.TotalSeconds, mkv.Stdout, mkv.Stderr);
        }
        var pip = Path.Combine(venv, "bin", "pip");
        var reqPath = d.PrimaryEntry ?? Path.Combine(repoDir, "requirements.txt");
        var args = File.Exists(reqPath)
            ? new List<string> { "install", "--upgrade", "pip", "wheel", "setuptools" }
            : new List<string> { "install", "--upgrade", "pip" };
        var upgrade = await ProcessRunner.RunAsync(pip, args, repoDir, BuildTimeout, cancellationToken: ct);
        if (upgrade.ExitCode != 0)
        {
            return new StepOutcome("pip", "pip install --upgrade pip wheel setuptools", repoDir, upgrade.ExitCode, false, upgrade.Duration.TotalSeconds, upgrade.Stdout, upgrade.Stderr);
        }
        if (File.Exists(reqPath))
        {
            var install = await ProcessRunner.RunAsync(pip, new[] { "install", "-r", reqPath }, repoDir, BuildTimeout, cancellationToken: ct);
            return new StepOutcome("pip", $"pip install -r {Path.GetRelativePath(repoDir, reqPath)}", repoDir,
                install.ExitCode, install.ExitCode == 0 && !install.TimedOut, install.Duration.TotalSeconds, install.Stdout, install.Stderr);
        }
        return new StepOutcome("pip", "pip install --upgrade pip", repoDir, 0, true, upgrade.Duration.TotalSeconds, upgrade.Stdout, upgrade.Stderr);
    }

    private async Task<StepOutcome> RunPythonImportSmokeAsync(string repoDir, DetectedToolchain d, CancellationToken ct)
    {
        var py = Path.Combine(repoDir, ".arm-venv", "bin", "python");
        var script = "import sys, importlib, glob, os\n"
            + "root = os.getcwd()\n"
            + "top = sorted({os.path.splitext(os.path.basename(p))[0] for p in glob.glob(os.path.join(root, '*.py')) if not os.path.basename(p).startswith('_')})[:5]\n"
            + "results = []\n"
            + "for m in top:\n"
            + "    try:\n"
            + "        importlib.import_module(m); results.append((m, 'ok'))\n"
            + "    except Exception as e: results.append((m, f'err: {type(e).__name__}: {e}'))\n"
            + "for m, s in results: print(f'{m}: {s}')\n"
            + "sys.exit(0 if all(s == 'ok' for _, s in results) else 1)\n";
        var scriptPath = Path.Combine(repoDir, ".arm-smoke.py");
        await File.WriteAllTextAsync(scriptPath, script, ct);
        var r = await ProcessRunner.RunAsync(py, new[] { scriptPath }, repoDir, TestTimeout, cancellationToken: ct);
        try { File.Delete(scriptPath); } catch { }
        return new StepOutcome("python", "python .arm-smoke.py", repoDir, r.ExitCode, r.ExitCode == 0 && !r.TimedOut, r.Duration.TotalSeconds, r.Stdout, r.Stderr);
    }

    private async Task<StepOutcome> RunNodeInstallAsync(string repoDir, DetectedToolchain d, CancellationToken ct)
    {
        var lockPath = Path.Combine(repoDir, "package-lock.json");
        var cmd = File.Exists(lockPath) ? "ci" : "install";
        var r = await ProcessRunner.RunAsync("npm", new[] { cmd, "--no-audit", "--no-fund" }, repoDir, BuildTimeout, cancellationToken: ct);
        return new StepOutcome("npm", $"npm {cmd}", repoDir, r.ExitCode, r.ExitCode == 0 && !r.TimedOut, r.Duration.TotalSeconds, r.Stdout, r.Stderr);
    }

    private async Task<StepOutcome> RunNodeTestAsync(string repoDir, DetectedToolchain d, CancellationToken ct)
    {
        var r = await ProcessRunner.RunAsync("npm", new[] { "test", "--if-present" }, repoDir, TestTimeout, cancellationToken: ct);
        return new StepOutcome("npm", "npm test --if-present", repoDir, r.ExitCode, r.ExitCode == 0 && !r.TimedOut, r.Duration.TotalSeconds, r.Stdout, r.Stderr);
    }

    private static async Task<ProcessResult> RunGitAsync(string workingDirectory, IReadOnlyList<string> arguments, CancellationToken ct)
    {
        return await ProcessRunner.RunAsync("git", arguments, workingDirectory, GitTimeout,
            extraEnv: new Dictionary<string, string?>
            {
                ["GIT_TERMINAL_PROMPT"] = "0",
                ["GIT_ASKPASS"] = "/bin/echo",
            }, cancellationToken: ct);
    }

    private static void MarkBuild(ProgressTracker progress, StepOutcome? outcome)
    {
        if (outcome is { Succeeded: true }) progress.Succeed("build", $"{outcome.DurationSeconds:F0}s");
        else progress.Fail("build", outcome is null ? "build failed" : Trim(outcome.StderrTail));
    }

    private static void MarkTests(ProgressTracker progress, StepOutcome? outcome)
    {
        if (outcome is { Succeeded: true }) progress.Succeed("tests", $"{outcome.DurationSeconds:F0}s");
        else progress.Fail("tests", outcome is null ? "tests failed" : Trim(outcome.StderrTail));
    }

    private static void EnsureOk(ProcessResult r, string what)
    {
        if (r.ExitCode == 0 && !r.TimedOut) return;
        throw new InvalidOperationException($"{what} failed (exit={r.ExitCode}, timedOut={r.TimedOut}): {Trim(r.Stderr)}");
    }

    private static string Trim(string s) => s.Length <= 400 ? s : s[..400] + "…";

    private static string Sanitize(string id)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(id.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch { /* best effort */ }
    }

    private static string BuildSummary(PatchApplicationReport patches, StepOutcome? build, StepOutcome? tests, ToolchainKind kind)
    {
        var buildStatus = build is null ? "n/a" : (build.Succeeded ? "PASS" : "FAIL");
        var testStatus = tests is null ? "n/a" : (tests.Succeeded ? "PASS" : "FAIL");
        return $"toolchain={kind} · applied={patches.Applied.Count}/{patches.Applied.Count + patches.Rejected.Count} · build={buildStatus} · tests={testStatus}";
    }
}
