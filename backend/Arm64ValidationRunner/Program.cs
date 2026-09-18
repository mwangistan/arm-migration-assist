using Arm64ValidationRunner;
using Microsoft.AspNetCore.Http.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

var options = new RunnerOptions
{
    BearerToken = Environment.GetEnvironmentVariable("ARM64_RUNNER_BEARER_TOKEN") ?? string.Empty,
    VmSku = Environment.GetEnvironmentVariable("ARM64_RUNNER_VM_SKU") ?? "Standard_D4ps_v6",
    Region = Environment.GetEnvironmentVariable("ARM64_RUNNER_REGION") ?? "eastus2",
    WorkRoot = Environment.GetEnvironmentVariable("ARM64_RUNNER_WORK_ROOT") ?? "/var/lib/arm-validation-runner/work",
    PublicBaseUrl = Environment.GetEnvironmentVariable("ARM64_RUNNER_PUBLIC_URL") ?? string.Empty,
};
Directory.CreateDirectory(options.WorkRoot);

builder.Services.AddSingleton(options);
builder.Services.AddSingleton<JobStore>();
builder.Services.AddSingleton<JobExecutor>();
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    o.SerializerOptions.PropertyNameCaseInsensitive = true;
});

var listenUrl = Environment.GetEnvironmentVariable("ARM64_RUNNER_LISTEN_URL") ?? "http://0.0.0.0:8080";
builder.WebHost.UseUrls(listenUrl);

var app = builder.Build();
app.UseExceptionHandler();

app.MapGet("/healthz", () => Results.Ok(new { status = "ok", vmSku = options.VmSku, region = options.Region }));

app.MapPost("/api/v1/arm64/runs", (
    HttpRequest http,
    RunRequest? request,
    JobStore store,
    JobExecutor executor,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    if (!string.IsNullOrWhiteSpace(options.BearerToken))
    {
        var header = http.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.Ordinal) ||
            !string.Equals(header.AsSpan(7).ToString(), options.BearerToken, StringComparison.Ordinal))
        {
            return Results.Problem("Unauthorized.", statusCode: StatusCodes.Status401Unauthorized);
        }
    }
    if (request is null) return Results.Problem("Body is required.", statusCode: StatusCodes.Status400BadRequest);

    var job = store.Create();
    logger.LogInformation("Accepted run {JobId} for {Url}@{Sha} ({Patches} patches)",
        job.JobId, request.SourceUrl, request.BaseCommitSha, request.Patches?.Count ?? 0);

    _ = Task.Run(() => executor.ExecuteAsync(job, request, CancellationToken.None));

    var statusUrl = string.IsNullOrWhiteSpace(options.PublicBaseUrl)
        ? $"/api/v1/arm64/runs/{job.JobId}"
        : $"{options.PublicBaseUrl.TrimEnd('/')}/api/v1/arm64/runs/{job.JobId}";
    var envelope = new JobEnvelope(job.JobId, job.Status, statusUrl);
    return Results.Accepted(statusUrl, envelope);
});

app.MapGet("/api/v1/arm64/runs/{jobId}", (string jobId, HttpRequest http, JobStore store) =>
{
    if (!string.IsNullOrWhiteSpace(options.BearerToken))
    {
        var header = http.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.Ordinal) ||
            !string.Equals(header.AsSpan(7).ToString(), options.BearerToken, StringComparison.Ordinal))
        {
            return Results.Problem("Unauthorized.", statusCode: StatusCodes.Status401Unauthorized);
        }
    }
    if (!store.TryGet(jobId, out var record))
    {
        return Results.Problem("Job not found.", statusCode: StatusCodes.Status404NotFound);
    }
    var status = new JobStatus(record.JobId, record.Status, record.CreatedAt, record.StartedAt, record.FinishedAt, record.Scorecard, record.Error);
    return Results.Ok(status);
});

app.Run();

public partial class Program;
