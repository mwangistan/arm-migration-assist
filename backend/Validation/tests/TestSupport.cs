using Validation.BuildValidation;
using Xunit;

namespace Validation.Tests;

internal sealed class TestWorkspace : IDisposable
{
    public string Root { get; }
    public string Repo { get; }
    public string Evidence { get; }

    public TestWorkspace()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null && !Directory.Exists(Path.Combine(current.FullName, "backend", "Validation")))
            current = current.Parent;
        if (current is null) throw new InvalidOperationException("Tests must run from the repository.");
        Root = Path.Combine(current.FullName, "artifacts", "validation-tests", Guid.NewGuid().ToString("N"));
        Repo = Path.Combine(Root, "repo");
        Evidence = Path.Combine(Root, "evidence");
        Directory.CreateDirectory(Repo);
    }

    public RepositoryContext Context(params (string Path, string Content)[] artifacts) =>
        new(Repo, new string('a', 40), "migration",
            artifacts.Select(artifact => new RepositoryArtifact(artifact.Path, new string('b', 64), artifact.Content)).ToArray(), []);

    public PreparedValidation Plan(params ValidationCommand[] commands) => new(
        "1.0", "validation-plan", "migration-plan", Context(), Evidence, [],
        [new("validation:vc-build", CheckSource.ValidationCheck, "vc-build", null, "build",
            "Build the application", "Build succeeds", ["ev-source"], ["guide-build"])],
        commands.Length == 0 ? [Command()] : commands, [], []);

    public static ValidationCommand Command(string id = "build", IReadOnlyList<string>? dependencies = null) => new(
        id, "Approved check", CommandKind.Custom, ExecutionSurface.Unspecified,
        "fake-tool", ["check"], ".", new Dictionary<string, string>(), 60, dependencies ?? [], ["validation:vc-build"]);

    public static MigrationPlan Migration() => new("1.0", "migration-plan",
        new(["arm64-vm"], [new("vc-build", "Build succeeds", "ARM64 build succeeds", ["ev-build"], ["guide-build"])],
            [new("vc-function", "Core scenario", "Expected output")], [], [], [], [], [], []),
        [new("wi-one", [new("at-one", "Acceptance scenario", "Expected behavior")]),
         new("wi-two", [new("at-one", "Another acceptance scenario", "Expected behavior")])]);

    public static PlanApproval Approve(PreparedValidation plan, params string[] ids) =>
        new(PlanSafety.Fingerprint(plan), ids.Length == 0 ? plan.Commands.Select(command => command.Id).ToArray() : ids);

    public void Dispose()
    {
        if (!Directory.Exists(Root)) return;
        foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(Root, recursive: true);
    }
}

internal sealed class FakeInspector(RepositoryContext context) : IRepositoryInspector
{
    public int Calls { get; private set; }
    public Exception? Error { get; set; }
    public RepositoryContext Context { get; set; } = context;
    public Task<RepositoryContext> InspectAsync(RepositoryTarget target, CancellationToken cancellationToken)
    {
        Calls++;
        if (Error is not null) throw Error;
        return Task.FromResult(Context);
    }
}

internal sealed class FakeProcessRunner : IProcessRunner
{
    public List<ProcessInvocation> Calls { get; } = [];
    public Func<ProcessInvocation, ProcessOutcome> Execute { get; set; } = _ => Outcome();

    public Task<ProcessOutcome> RunAsync(ProcessInvocation invocation, CancellationToken cancellationToken)
    {
        Calls.Add(invocation);
        return Task.FromResult(Execute(invocation));
    }

    public static ProcessOutcome Outcome(int? exitCode = 0, string stdout = "", string stderr = "",
        string? startError = null, bool timedOut = false, bool cancelled = false) =>
        new(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, exitCode, stdout, stderr, startError, timedOut, cancelled);
}

internal sealed class IsolatedGitProcessRunner(TestWorkspace workspace) : IProcessRunner
{
    private readonly LocalProcessRunner runner = new();
    public List<ProcessInvocation> Calls { get; } = [];

    public Task<ProcessOutcome> RunAsync(ProcessInvocation invocation, CancellationToken cancellationToken)
    {
        // Fixtures and CLI children must not depend on machine-wide filters or user configuration.
        var environment = new Dictionary<string, string>(invocation.Environment)
        {
            ["GIT_CONFIG_GLOBAL"] = Path.Combine(workspace.Root, "absent-gitconfig"),
            ["GIT_CONFIG_NOSYSTEM"] = "1"
        };
        Calls.Add(invocation);
        return runner.RunAsync(invocation with { Environment = environment }, cancellationToken);
    }
}

internal sealed class FakePlanner(Func<ValidationPlannerRequest, ValidationPlannerResponse> respond) : IValidationPlanner
{
    public Task<ValidationPlannerResponse> PlanAsync(ValidationPlannerRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(respond(request));
}
internal sealed class FakeAnalyzer(Func<EvidenceAnalysisRequest, EvidenceAnalysisResponse> respond) : IEvidenceAnalyzer
{
    public Task<EvidenceAnalysisResponse> AnalyzeAsync(EvidenceAnalysisRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(respond(request));
}
internal sealed class FakeReviewer(Func<CoverageReviewRequest, CoverageReviewResponse> respond) : ICoverageReviewer
{
    public Task<CoverageReviewResponse> ReviewAsync(CoverageReviewRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(respond(request));
}
