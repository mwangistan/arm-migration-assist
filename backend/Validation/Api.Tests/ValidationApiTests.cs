using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Validation.Api;
using Validation.BuildValidation;
using Validation.Dashboard;
using Xunit;

namespace Validation.Api.Tests;

public sealed class ValidationApiTests
{
    [Fact]
    public async Task PlanCreateRetrieveAndApprovalUseStoredFingerprint()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var create = await CreatePlanAsync(client, includeProposal: true);
        Assert.NotNull(create.Proposal);
        var fetched = await client.GetFromJsonAsync<PlanResponse>(create.Links.Self, ValidationJson.Options);
        Assert.Equal(create.PlanId, fetched!.PlanId);

        var conflict = await client.PutAsJsonAsync(create.Links.Approval,
            new UpdateApprovalRequest(["build"], PlanFingerprint: "wrong"), ValidationJson.Options);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);

        var approval = await PutApprovalAsync(client, create, ["build"]);
        Assert.Equal(create.Fingerprint, approval.Approval.PlanFingerprint);
        Assert.Equal(["build"], approval.Approval.ApprovedCommandIds);
    }

    [Fact]
    public async Task QueueRunCompletesAndExposesReportAndDashboard()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var plan = await CreatePlanAsync(client);
        await PutApprovalAsync(client, plan, ["build"]);

        var queued = await client.PostAsync(plan.Links.Runs, null);
        Assert.Equal(HttpStatusCode.Accepted, queued.StatusCode);
        var run = (await queued.Content.ReadFromJsonAsync<RunResponse>(ValidationJson.Options))!;

        run = await WaitForTerminalAsync(client, run.RunId);
        Assert.Equal(ValidationRunStatus.Completed, run.Status);
        Assert.Null(run.Error);

        var report = await client.GetFromJsonAsync<ValidationReport>(run.Links.Report, ValidationJson.Options);
        var dashboard = await client.GetFromJsonAsync<ValidationDashboard>(run.Links.Dashboard, ValidationJson.Options);
        Assert.Equal(run.RunId, report!.RunId);
        Assert.Equal(report.RunId, dashboard!.RunId);
        Assert.Equal(OverallStatus.Validated, report.Scorecard.Status);
    }

    [Fact]
    public async Task ReportAndDashboardReturnConflictUntilCompleteAndMissingIdsReturnNotFound()
    {
        using var factory = new ApiFactory();
        factory.Workflow.BlockRuns = true;
        using var client = factory.CreateClient();
        var plan = await CreatePlanAsync(client);
        await PutApprovalAsync(client, plan, ["build"]);
        var queued = await client.PostAsync(plan.Links.Runs, null);
        var run = (await queued.Content.ReadFromJsonAsync<RunResponse>(ValidationJson.Options))!;

        var early = await client.GetAsync(run.Links.Report);
        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);
        var earlyDetail = await ProblemDetailAsync(early);
        Assert.Contains("available only after the run completes", earlyDetail, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/validation/plans/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/validation/runs/bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb")).StatusCode);

        factory.Workflow.Release.TrySetResult();
        await WaitForTerminalAsync(client, run.RunId);
    }

    [Fact]
    public async Task EmptyApprovalIsAllowedAndRunsNoCommands()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var plan = await CreatePlanAsync(client);

        // An explicitly stored empty approval is allowed and remains distinct from "never approved".
        await PutApprovalAsync(client, plan, []);

        var queued = await client.PostAsync(plan.Links.Runs, null);
        Assert.Equal(HttpStatusCode.Accepted, queued.StatusCode);
        var run = (await queued.Content.ReadFromJsonAsync<RunResponse>(ValidationJson.Options))!;

        run = await WaitForTerminalAsync(client, run.RunId);
        var report = await client.GetFromJsonAsync<ValidationReport>(run.Links.Report, ValidationJson.Options);
        Assert.Empty(factory.Workflow.RunApprovals.Single().ApprovedCommandIds);
        Assert.Equal(ResultStatus.NotRun, report!.Commands.Single().Status);
    }

    [Fact]
    public async Task QueueingARunBeforeAnyApprovalIsStoredReturnsConflict()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var plan = await CreatePlanAsync(client);

        // No PUT to /approval has ever happened; GET synthesizes a display-only skeleton...
        var approval = await client.GetFromJsonAsync<ApprovalResponse>(plan.Links.Approval, ValidationJson.Options);
        Assert.Empty(approval!.Approval.ApprovedCommandIds);

        // ...but that skeleton must not let a run be queued.
        var queued = await client.PostAsync(plan.Links.Runs, null);
        Assert.Equal(HttpStatusCode.Conflict, queued.StatusCode);
        var detail = await ProblemDetailAsync(queued);
        Assert.Contains("approval", detail, StringComparison.OrdinalIgnoreCase);

        // Explicitly storing an (even empty) approval clears the gate.
        await PutApprovalAsync(client, plan, []);
        var retry = await client.PostAsync(plan.Links.Runs, null);
        Assert.Equal(HttpStatusCode.Accepted, retry.StatusCode);
    }

    [Fact]
    public async Task ApprovalChangedAfterQueueingDoesNotAffectTheAlreadyQueuedRun()
    {
        using var factory = new ApiFactory();
        factory.Workflow.BlockRuns = true;
        using var client = factory.CreateClient();
        var blocker = await CreatePlanAsync(client);
        await PutApprovalAsync(client, blocker, []);
        var blockingResponse = await client.PostAsync(blocker.Links.Runs, null);
        Assert.Equal(HttpStatusCode.Accepted, blockingResponse.StatusCode);
        await factory.Workflow.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var plan = await CreatePlanAsync(client);
        await PutApprovalAsync(client, plan, ["build"]);

        var queued = await client.PostAsync(plan.Links.Runs, null);
        Assert.Equal(HttpStatusCode.Accepted, queued.StatusCode);
        var run = (await queued.Content.ReadFromJsonAsync<RunResponse>(ValidationJson.Options))!;

        Assert.Equal(ValidationRunStatus.Queued,
            (await client.GetFromJsonAsync<RunResponse>(run.Links.Status, ValidationJson.Options))!.Status);
        await PutApprovalAsync(client, plan, []);
        var snapshot = await factory.Services.GetRequiredService<IValidationStore>().GetRunSnapshotAsync(run.RunId, CancellationToken.None);
        Assert.Equal(["build"], snapshot!.Approval.ApprovedCommandIds);
        factory.Workflow.Release.TrySetResult();

        run = await WaitForTerminalAsync(client, run.RunId);
        Assert.Equal(ValidationRunStatus.Completed, run.Status);

        var executedApproval = factory.Workflow.RunApprovals.Last();
        Assert.Contains("build", executedApproval.ApprovedCommandIds);
        var report = await client.GetFromJsonAsync<ValidationReport>(run.Links.Report, ValidationJson.Options);
        Assert.Equal(ResultStatus.Passed, report!.Commands.Single().Status);
    }

    [Fact]
    public async Task RecoveryRequeuesQueuedRunsAndFailsInterruptedRuns()
    {
        using var factory = new ApiFactory();
        var store = factory.Services.GetRequiredService<IValidationStore>();
        var prepared = TestData.Prepared();
        var created = await store.CreatePlanAsync(prepared, CancellationToken.None);
        var approval = new PlanApproval(created.Metadata.Fingerprint, ["build"]);
        await store.SaveApprovalAsync(created.Metadata.PlanId, approval, CancellationToken.None);
        var queued = await store.CreateRunAsync(created.Metadata.PlanId, prepared, approval, CancellationToken.None);
        Assert.NotNull(queued);

        var createdSecond = await store.CreatePlanAsync(prepared, CancellationToken.None);
        var approvalSecond = new PlanApproval(createdSecond.Metadata.Fingerprint, ["build"]);
        await store.SaveApprovalAsync(createdSecond.Metadata.PlanId, approvalSecond, CancellationToken.None);
        var running = await store.CreateRunAsync(createdSecond.Metadata.PlanId, prepared, approvalSecond, CancellationToken.None);
        Assert.NotNull(running);
        await store.MarkRunRunningAsync(running!.RunId, CancellationToken.None);

        var recovered = await store.RecoverRunsAsync(CancellationToken.None);
        var interrupted = await store.GetRunAsync(running.RunId, CancellationToken.None);

        Assert.Contains(queued!.RunId, recovered);
        Assert.Equal(ValidationRunStatus.Failed, interrupted!.Status);
        Assert.Contains("interrupted", interrupted.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RecoveryDoesNotDeadlockWhenQueuedBacklogExceedsChannelCapacity()
    {
        using var factory = new ApiFactory(queueCapacity: 2);
        var seedStore = new FileValidationStore(
            Microsoft.Extensions.Options.Options.Create(new ValidationApiOptions { StorageRoot = factory.StorageRoot }),
            new FakeHostEnvironment());

        var runIds = new List<string>();
        for (int i = 0; i < 5; i++)
        {
            var prepared = TestData.Prepared();
            var created = await seedStore.CreatePlanAsync(prepared, CancellationToken.None);
            var approval = new PlanApproval(created.Metadata.Fingerprint, ["build"]);
            await seedStore.SaveApprovalAsync(created.Metadata.PlanId, approval, CancellationToken.None);
            var run = await seedStore.CreateRunAsync(created.Metadata.PlanId, prepared, approval, CancellationToken.None);
            Assert.NotNull(run);
            runIds.Add(run!.RunId);
        }

        // Building the TestServer/host here starts the hosted worker, which recovers all five
        // queued runs into a channel whose capacity is only 2. Before the fix this deadlocked:
        // the reader never started until recovery (which blocked on channel capacity) finished.
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/healthz")).StatusCode);

        var completion = Task.Run(async () =>
        {
            foreach (var runId in runIds)
                await WaitForTerminalAsync(client, runId);
        });
        var finished = await Task.WhenAny(completion, Task.Delay(TimeSpan.FromSeconds(30)));
        Assert.Same(completion, finished);
        await completion;
    }

    [Theory]
    [InlineData("{ not valid json ][")]
    [InlineData("null")]
    [InlineData("{}")]
    public async Task RecoverySkipsAnUnreadableRunRecordAndStillRecoversTheRest(string corruptJson)
    {
        using var factory = new ApiFactory();
        var seedStore = new FileValidationStore(
            Microsoft.Extensions.Options.Options.Create(new ValidationApiOptions { StorageRoot = factory.StorageRoot }),
            new FakeHostEnvironment());
        var prepared = TestData.Prepared();
        var created = await seedStore.CreatePlanAsync(prepared, CancellationToken.None);
        var approval = new PlanApproval(created.Metadata.Fingerprint, ["build"]);
        await seedStore.SaveApprovalAsync(created.Metadata.PlanId, approval, CancellationToken.None);
        var goodRun = await seedStore.CreateRunAsync(created.Metadata.PlanId, prepared, approval, CancellationToken.None);
        Assert.NotNull(goodRun);

        string corruptRunId = Guid.NewGuid().ToString("N");
        string corruptDirectory = Path.Combine(factory.StorageRoot, "runs", corruptRunId);
        Directory.CreateDirectory(Path.Combine(corruptDirectory, "outputs"));
        await File.WriteAllTextAsync(Path.Combine(corruptDirectory, "run.json"), corruptJson);

        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/healthz")).StatusCode);

        var run = await WaitForTerminalAsync(client, goodRun!.RunId);
        Assert.Equal(ValidationRunStatus.Completed, run.Status);
    }

    [Fact]
    public async Task FileStoreReloadsPersistedPlanApprovalAndRunMetadata()
    {
        string root = Path.Combine(Path.GetTempPath(), "validation-api-store-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var options = Microsoft.Extensions.Options.Options.Create(new ValidationApiOptions { StorageRoot = root });
            var environment = new FakeHostEnvironment();
            var first = new FileValidationStore(options, environment);
            var created = await first.CreatePlanAsync(TestData.Prepared(), CancellationToken.None);
            var approval = new PlanApproval(created.Metadata.Fingerprint, ["build"]);
            await first.SaveApprovalAsync(created.Metadata.PlanId, approval, CancellationToken.None);
            var run = await first.CreateRunAsync(created.Metadata.PlanId, TestData.Prepared(), approval, CancellationToken.None);
            Assert.NotNull(run);

            var second = new FileValidationStore(options, environment);
            var loadedPlan = await second.GetPlanAsync(created.Metadata.PlanId, CancellationToken.None);
            var loadedRun = await second.GetRunAsync(run!.RunId, CancellationToken.None);

            Assert.Equal(created.Metadata.Fingerprint, loadedPlan!.Approval!.PlanFingerprint);
            Assert.Equal(["build"], loadedPlan.Approval.ApprovedCommandIds);
            Assert.Equal(ValidationRunStatus.Queued, loadedRun!.Status);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PlanCannotBeReusedEvenAfterTheFirstRunCompletes()
    {
        using var factory = new ApiFactory(workflowDelay: TimeSpan.FromMilliseconds(300));
        using var client = factory.CreateClient();
        var plan = await CreatePlanAsync(client);
        await PutApprovalAsync(client, plan, ["build"]);

        var first = await client.PostAsync(plan.Links.Runs, null);
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);

        var second = await client.PostAsync(plan.Links.Runs, null);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        var firstRun = (await first.Content.ReadFromJsonAsync<RunResponse>(ValidationJson.Options))!;
        await WaitForTerminalAsync(client, firstRun.RunId);

        var third = await client.PostAsync(plan.Links.Runs, null);
        Assert.Equal(HttpStatusCode.Conflict, third.StatusCode);
        Assert.Contains("new plan", await ProblemDetailAsync(third));
    }

    [Fact]
    public async Task QueueFullReturns503AndDoesNotOrphanTheNewRun()
    {
        using var factory = new ApiFactory(queueCapacity: 1);
        factory.Workflow.BlockRuns = true;
        using var client = factory.CreateClient();

        var planA = await CreatePlanAsync(client);
        await PutApprovalAsync(client, planA, ["build"]);
        var planB = await CreatePlanAsync(client);
        await PutApprovalAsync(client, planB, ["build"]);
        var planC = await CreatePlanAsync(client);
        await PutApprovalAsync(client, planC, ["build"]);

        var runA = await client.PostAsync(planA.Links.Runs, null);
        Assert.Equal(HttpStatusCode.Accepted, runA.StatusCode);
        var runAIdEarly = (await runA.Content.ReadFromJsonAsync<RunResponse>(ValidationJson.Options))!.RunId;
        await factory.Workflow.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var runB = await client.PostAsync(planB.Links.Runs, null);
        Assert.Equal(HttpStatusCode.Accepted, runB.StatusCode); // occupies the only free channel slot while A executes

        var runC = await client.PostAsync(planC.Links.Runs, null);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, runC.StatusCode);
        var detail = await ProblemDetailAsync(runC);
        Assert.Contains("queue is full", detail, StringComparison.OrdinalIgnoreCase);
        var records = Directory.GetFiles(Path.Combine(factory.StorageRoot, "runs"), "run.json", SearchOption.AllDirectories)
            .Select(path => ValidationJson.Deserialize<ValidationRunRecord>(File.ReadAllText(path))).ToArray();
        Assert.Equal(ValidationRunStatus.Failed, records.Single(record => record.PlanId == planC.PlanId).Status);
        factory.Workflow.Release.TrySetResult();

        var runBId = (await runB.Content.ReadFromJsonAsync<RunResponse>(ValidationJson.Options))!.RunId;
        Assert.Equal(ValidationRunStatus.Completed, (await WaitForTerminalAsync(client, runAIdEarly)).Status);
        Assert.Equal(ValidationRunStatus.Completed, (await WaitForTerminalAsync(client, runBId)).Status);

        var retryC = await client.PostAsync(planC.Links.Runs, null);
        Assert.Equal(HttpStatusCode.Conflict, retryC.StatusCode);
        var freshPlan = await CreatePlanAsync(client);
        await PutApprovalAsync(client, freshPlan, ["build"]);
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsync(freshPlan.Links.Runs, null)).StatusCode);
    }

    [Fact]
    public async Task FailedRunReportAndDashboardStateTerminalConflictNotPendingConflict()
    {
        using var factory = new ApiFactory();
        factory.Workflow.ThrowOnRun = true;
        using var client = factory.CreateClient();
        var plan = await CreatePlanAsync(client);
        await PutApprovalAsync(client, plan, ["build"]);

        var queued = await client.PostAsync(plan.Links.Runs, null);
        var run = (await queued.Content.ReadFromJsonAsync<RunResponse>(ValidationJson.Options))!;
        run = await WaitForTerminalAsync(client, run.RunId);
        Assert.Equal(ValidationRunStatus.Failed, run.Status);
        Assert.DoesNotContain("secret", run.Error!, StringComparison.OrdinalIgnoreCase);

        var report = await client.GetAsync(run.Links.Report);
        Assert.Equal(HttpStatusCode.Conflict, report.StatusCode);
        var reportDetail = await ProblemDetailAsync(report);
        Assert.Contains("failed", reportDetail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("available only after the run completes", reportDetail, StringComparison.OrdinalIgnoreCase);

        var dashboard = await client.GetAsync(run.Links.Dashboard);
        Assert.Equal(HttpStatusCode.Conflict, dashboard.StatusCode);
        var dashboardDetail = await ProblemDetailAsync(dashboard);
        Assert.Contains("failed", dashboardDetail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("available only after the run completes", dashboardDetail, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("relative\\evidence")]
    public async Task CreatePlanRejectsARelativeEvidenceDirectory(string evidenceDirectory)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/validation/plans",
            new CreatePlanRequest(TestData.Migration(), new("C:\\target-repo"), new(evidenceDirectory)), ValidationJson.Options);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreatePlanRejectsAnEvidenceDirectoryInsideTheTargetRepository()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/validation/plans",
            new CreatePlanRequest(TestData.Migration(), new("C:\\target-repo"), new("C:\\target-repo\\evidence")), ValidationJson.Options);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreatePlanRejectsAnEvidenceDirectoryInsideTheApiStorageRoot()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/validation/plans",
            new CreatePlanRequest(TestData.Migration(), new("C:\\target-repo"), new(Path.Combine(factory.StorageRoot, "evidence"))),
            ValidationJson.Options);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreatePlanWithMissingRequiredFieldsReturnsBadRequestNotServerError()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var response = await client.PostAsync("/api/v1/validation/plans", new StringContent("{}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public void ExceptionMappingHidesAllUnhandledInternalDetail()
    {
        var (badRequestStatus, badRequestDetail) = ExceptionMapping.Classify(new InvalidDataException("Bad input."));
        Assert.Equal(StatusCodes.Status500InternalServerError, badRequestStatus);
        Assert.Equal(ExceptionMapping.GenericServerErrorDetail, badRequestDetail);

        var (ioStatus, ioDetail) = ExceptionMapping.Classify(new IOException(@"Disk failure at C:\secret\server\data\metadata.json"));
        Assert.Equal(StatusCodes.Status500InternalServerError, ioStatus);
        Assert.DoesNotContain("secret", ioDetail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"C:\", ioDetail);

        var (authStatus, authDetail) = ExceptionMapping.Classify(new UnauthorizedAccessException(@"Access to C:\secret\path denied."));
        Assert.Equal(StatusCodes.Status500InternalServerError, authStatus);
        Assert.DoesNotContain("secret", authDetail, StringComparison.OrdinalIgnoreCase);

        var (nullRefStatus, _) = ExceptionMapping.Classify(new NullReferenceException());
        Assert.Equal(StatusCodes.Status500InternalServerError, nullRefStatus);
    }

    [Fact]
    public async Task LoopbackMiddlewareAllowsTestServerNullRemoteIpAndRejectsNonLoopback()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/healthz")).StatusCode);

        var middleware = new LoopbackOnlyMiddleware(_ => Task.CompletedTask,
            Microsoft.Extensions.Options.Options.Create(new ValidationApiOptions()));
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddProblemDetails();
        context.RequestServices = services.BuildServiceProvider();
        context.Connection.RemoteIpAddress = IPAddress.Parse("10.1.2.3");
        await middleware.InvokeAsync(context);
        Assert.Equal(403, context.Response.StatusCode);
    }

    private static async Task<string> ProblemDetailAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.TryGetProperty("detail", out var detail) ? detail.GetString() ?? "" : "";
    }

    private static async Task<PlanResponse> CreatePlanAsync(HttpClient client, bool includeProposal = false)
    {
        var response = await client.PostAsJsonAsync("/api/v1/validation/plans",
            new CreatePlanRequest(TestData.Migration(), new(Path.Combine(Path.GetTempPath(), "target-repo")),
                new(Path.Combine(Path.GetTempPath(), "evidence")), includeProposal),
            ValidationJson.Options);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PlanResponse>(ValidationJson.Options))!;
    }

    private static async Task<ApprovalResponse> PutApprovalAsync(HttpClient client, PlanResponse plan, IReadOnlyList<string> approved)
    {
        var response = await client.PutAsJsonAsync(plan.Links.Approval, new UpdateApprovalRequest(approved), ValidationJson.Options);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ApprovalResponse>(ValidationJson.Options))!;
    }

    private static async Task<RunResponse> WaitForTerminalAsync(HttpClient client, string runId)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var run = await client.GetFromJsonAsync<RunResponse>($"/api/v1/validation/runs/{runId}", ValidationJson.Options);
            if (run!.Status is ValidationRunStatus.Completed or ValidationRunStatus.Failed or ValidationRunStatus.Cancelled)
                return run;
            await Task.Delay(50);
        }
        throw new TimeoutException("Run did not complete.");
    }

}

internal sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly TimeSpan workflowDelay;
    private readonly int? queueCapacity;
    public string StorageRoot { get; } = Path.Combine(Path.GetTempPath(), "validation-api-tests", Guid.NewGuid().ToString("N"));
    public FakeWorkflow Workflow { get; }
    public Action<IServiceCollection>? ConfigureServices { get; set; }

    public ApiFactory(TimeSpan? workflowDelay = null, int? queueCapacity = null)
    {
        this.workflowDelay = workflowDelay ?? TimeSpan.Zero;
        this.queueCapacity = queueCapacity;
        Workflow = new FakeWorkflow(this.workflowDelay);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ValidationApi:StorageRoot", StorageRoot);
        if (queueCapacity is { } capacity)
            builder.UseSetting("ValidationApi:QueueCapacity", capacity.ToString());
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IValidationWorkflowRunner>(Workflow);
            ConfigureServices?.Invoke(services);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (Directory.Exists(StorageRoot))
            Directory.Delete(StorageRoot, recursive: true);
    }
}

internal sealed class FakeWorkflow(TimeSpan delay) : IValidationWorkflowRunner
{
    public List<PlanApproval> RunApprovals { get; } = [];
    public bool ThrowOnRun { get; set; }
    public bool BlockRuns { get; set; }
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<PreparedValidation> PrepareAsync(MigrationPlan migration, RepositoryTarget target, PlanningOptions options, CancellationToken cancellationToken) =>
        Task.FromResult(TestData.Prepared(evidenceDirectory: options.EvidenceDirectory));

    public async Task<ValidationReport> RunAsync(PreparedValidation prepared, PlanApproval approval, CancellationToken cancellationToken)
    {
        Started.TrySetResult();
        if (BlockRuns)
            await Release.Task.WaitAsync(cancellationToken);
        if (delay > TimeSpan.Zero)
            await Task.Delay(delay, cancellationToken);
        if (ThrowOnRun)
            throw new IOException(@"Simulated internal failure at C:\secret\server\metadata.json.");
        RunApprovals.Add(approval);
        var commandResults = prepared.Commands.Select(command =>
            approval.ApprovedCommandIds.Contains(command.Id)
                ? new CommandResult(command.Id, ResultStatus.Passed, "Passed by fake workflow.", [])
                : new CommandResult(command.Id, ResultStatus.NotRun, "Command was not approved.", [])).ToArray();
        var criteria = prepared.Criteria.Select(criterion => new CriterionResult(
            criterion,
            commandResults.Any(result => result.Status == ResultStatus.Passed) ? ResultStatus.Passed : ResultStatus.NotRun,
            commandResults.Any(result => result.Status == ResultStatus.Passed) ? "Passed by fake workflow." : "No command approved.",
            prepared.Commands.Select(command => command.Id).ToArray(),
            [])).ToArray();
        var passed = criteria.Count(result => result.Status == ResultStatus.Passed);
        var notRun = criteria.Count(result => result.Status == ResultStatus.NotRun);
        return new("1.0", Guid.NewGuid().ToString("N"), PlanSafety.Fingerprint(prepared), prepared.MigrationPlanId,
            prepared.Repository, new("windows", "x64", "test"), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            commandResults, [], new(passed > 0 ? OverallStatus.Validated : OverallStatus.NotValidated,
                passed, 0, notRun, 0, 0, criteria), [], new(null, null, []));
    }
}

internal static class TestData
{
    public static MigrationPlan Migration() => new("1.0", "migration-plan",
        new(["arm64-vm"], [new("vc-build", "Build succeeds", "ARM64 build succeeds", ["ev-build"], ["guide-build"])],
            [], [], [], [], [], [], []),
        [new("wi-one", [new("at-one", "Acceptance scenario", "Expected behavior")])]);

    public static PreparedValidation Prepared(string? evidenceDirectory = null) => new(
        "1.0",
        "prepared-original",
        "migration-plan",
        new(Path.Combine(Path.GetTempPath(), "target-repo"), new string('a', 40), "migration", [], []),
        evidenceDirectory ?? Path.Combine(Path.GetTempPath(), "evidence"),
        ["arm64-vm"],
        [new("validation:vc-build", CheckSource.ValidationCheck, "vc-build", null, "build",
            "Build succeeds", "ARM64 build succeeds", ["ev-build"], ["guide-build"])],
        [new("build", "Build", CommandKind.Custom, ExecutionSurface.Unspecified, "dotnet", ["--info"], ".", new Dictionary<string, string>(), 60, [], ["validation:vc-build"])],
        [],
        []);
}

internal sealed class FakeHostEnvironment : Microsoft.Extensions.Hosting.IHostEnvironment
{
    public string EnvironmentName { get; set; } = "Development";
    public string ApplicationName { get; set; } = "Validation.Api.Tests";
    public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}