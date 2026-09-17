using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Validation.Api;
using Validation.BuildValidation;
using Xunit;

namespace Validation.Api.Tests;

public sealed class ApiHardeningTests
{
    [Fact]
    public void StorageDefaultsToContentRootWithoutRepositoryDiscovery()
    {
        using var factory = new ApiFactory();
        var environment = new FakeHostEnvironment { ContentRootPath = factory.StorageRoot };
        var store = new FileValidationStore(Options.Create(new ValidationApiOptions()), environment);
        Assert.True(Directory.Exists(Path.Combine(factory.StorageRoot, "artifacts", "validation-api", "runs")));
        Assert.Throws<InvalidOperationException>(() => new FileValidationStore(
            Options.Create(new ValidationApiOptions { StorageRoot = "relative-storage" }), environment));
    }

    [Fact]
    public void ContainmentHandlesFilesystemRootsTraversalAndSiblingPrefixes()
    {
        string root = Path.GetPathRoot(Path.GetTempPath())!;
        string target = Path.Combine(root, "target");
        Assert.True(PathGuard.IsSameOrInside(root, target));
        Assert.True(PathGuard.IsSameOrInside(target, Path.Combine(target, "sub", "..")));
        Assert.False(PathGuard.IsSameOrInside(target, target + "-sibling"));
        Assert.False(PathGuard.IsSameOrInside(target, Path.Combine(target, "..", "outside")));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"migrationPlan\":{}}")]
    public async Task MissingAndNullPlanBodiesAre400(string json)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var response = await client.PostAsync("/api/v1/validation/plans",
            new StringContent(json, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("relative-path")]
    [InlineData("\0invalid")]
    public async Task InvalidTargetAndEvidencePathsAre400(string path)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        foreach (bool invalidTarget in new[] { true, false })
        {
            var request = new CreatePlanRequest(TestData.Migration(),
                new(invalidTarget ? path : Path.Combine(Path.GetTempPath(), "target")),
                new(invalidTarget ? Path.Combine(Path.GetTempPath(), "evidence") : path));
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(
                "/api/v1/validation/plans", request, ValidationJson.Options)).StatusCode);
        }
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{broken")]
    public async Task CorruptStoredPlanIsGeneric500(string corruptJson)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var store = factory.Services.GetRequiredService<IValidationStore>();
        var plan = await store.CreatePlanAsync(TestData.Prepared(), CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(factory.StorageRoot, "plans", plan.Metadata.PlanId, "metadata.json"), corruptJson);
        var response = await client.GetAsync($"/api/v1/validation/plans/{plan.Metadata.PlanId}");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains(ExceptionMapping.GenericServerErrorDetail, body);
        Assert.DoesNotContain(factory.StorageRoot, body);
    }

    [Fact]
    public async Task ApprovalStorageIoFailureIsGeneric500()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var store = factory.Services.GetRequiredService<IValidationStore>();
        var plan = await store.CreatePlanAsync(TestData.Prepared(), CancellationToken.None);
        Directory.CreateDirectory(Path.Combine(factory.StorageRoot, "plans", plan.Metadata.PlanId, "approval.json"));
        var response = await client.PutAsJsonAsync($"/api/v1/validation/plans/{plan.Metadata.PlanId}/approval",
            new UpdateApprovalRequest([]), ValidationJson.Options);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains(ExceptionMapping.GenericServerErrorDetail, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ClientAbortAfterRecordCreationStillEnqueuesTheRun()
    {
        using var abort = new CancellationTokenSource();
        using var factory = new ApiFactory();
        string? runId = null;
        factory.ConfigureServices = services => services.AddSingleton<IValidationStore>(provider =>
        {
            var proxy = DispatchProxy.Create<IValidationStore, CreationCallbackStore>();
            var callbackStore = (CreationCallbackStore)proxy;
            callbackStore.Inner = ActivatorUtilities.CreateInstance<FileValidationStore>(provider);
            callbackStore.Created = record =>
            {
                runId = record.RunId;
                abort.Cancel();
            };
            return proxy;
        });
        using var client = factory.CreateClient();
        var store = factory.Services.GetRequiredService<IValidationStore>();
        var prepared = TestData.Prepared();
        var plan = await store.CreatePlanAsync(prepared, CancellationToken.None);
        await store.SaveApprovalAsync(plan.Metadata.PlanId, new(plan.Metadata.Fingerprint, []), CancellationToken.None);
        try
        {
            await client.PostAsync($"/api/v1/validation/plans/{plan.Metadata.PlanId}/runs", null, abort.Token);
        }
        catch (OperationCanceledException) when (abort.IsCancellationRequested) { }
        Assert.NotNull(runId);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        ValidationRunRecord? run;
        do
        {
            run = await store.GetRunAsync(runId!, timeout.Token);
            if (run?.Status == ValidationRunStatus.Completed) break;
            await Task.Delay(20, timeout.Token);
        } while (true);
        Assert.Equal(ValidationRunStatus.Completed, run.Status);
        Assert.NotNull(await store.GetRunSnapshotAsync(runId!, timeout.Token));
    }

    [Fact]
    public async Task CancelledArtifactsAreTerminalConflicts()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var store = factory.Services.GetRequiredService<IValidationStore>();
        var prepared = TestData.Prepared();
        var plan = await store.CreatePlanAsync(prepared, CancellationToken.None);
        var run = await store.CreateRunAsync(plan.Metadata.PlanId, prepared, new(plan.Metadata.Fingerprint, []), CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(factory.StorageRoot, "runs", run!.RunId, "run.json"),
            ValidationJson.Serialize(run with { Status = ValidationRunStatus.Cancelled }));
        foreach (var artifact in new[] { "report", "dashboard" })
        {
            var response = await client.GetAsync($"/api/v1/validation/runs/{run.RunId}/{artifact}");
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Contains("cancelled", await response.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task QuarantiningARunDoesNotReleaseItsPlanReservation()
    {
        using var factory = new ApiFactory();
        var options = Options.Create(new ValidationApiOptions { StorageRoot = factory.StorageRoot });
        var store = new FileValidationStore(options, new FakeHostEnvironment());
        var prepared = TestData.Prepared();
        var plan = await store.CreatePlanAsync(prepared, CancellationToken.None);
        var approval = new PlanApproval(plan.Metadata.Fingerprint, []);
        var run = await store.CreateRunAsync(plan.Metadata.PlanId, prepared, approval, CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(factory.StorageRoot, "runs", run!.RunId, "run.json"), "null");
        var restarted = new FileValidationStore(options, new FakeHostEnvironment());
        Assert.Empty(await restarted.RecoverRunsAsync(CancellationToken.None));
        Assert.Null(await restarted.CreateRunAsync(plan.Metadata.PlanId, prepared, approval, CancellationToken.None));
        Assert.Single(Directory.GetFiles(Path.Combine(factory.StorageRoot, "runs", run.RunId), "run.json.corrupt-*"));
    }
}

public class CreationCallbackStore : DispatchProxy
{
    public IValidationStore Inner { get; set; } = null!;
    public Action<ValidationRunRecord> Created { get; set; } = null!;

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        var result = targetMethod!.Invoke(Inner, args);
        return targetMethod.Name == nameof(IValidationStore.CreateRunAsync)
            ? NotifyAsync((Task<ValidationRunRecord?>)result!)
            : result;
    }

    private async Task<ValidationRunRecord?> NotifyAsync(Task<ValidationRunRecord?> task)
    {
        var record = await task;
        if (record is not null) Created(record);
        return record;
    }
}
