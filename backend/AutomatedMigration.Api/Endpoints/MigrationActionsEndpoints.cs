using AutomatedMigration.Api.Contracts;
using AutomatedMigration.Api.Jobs;
using AutomatedMigration.Api.Validation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace AutomatedMigration.Api.Endpoints;

public static class MigrationActionsEndpoints
{
    public static IEndpointRouteBuilder MapMigrationActionsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/migration-actions", HandlePostAsync);
        endpoints.MapGet("/api/migration-actions/jobs/{jobId}", HandleGetAsync);
        endpoints.MapGet("/api/migration-actions/arm64-runs/{runId}", HandleArm64RunStatusAsync);
        return endpoints;
    }

    private static Results<Accepted<MigrationActionsAccepted>, BadRequest<ProblemDetails>> HandlePostAsync(
        MigrationActionsRequest? request,
        MigrationJobStore store,
        HttpContext http)
    {
        if (request is null)
        {
            return TypedResults.BadRequest(Problem("Request body is required."));
        }
        if (request.Plan is null)
        {
            return TypedResults.BadRequest(Problem("plan is required."));
        }
        if (string.IsNullOrWhiteSpace(request.Plan.PlanId))
        {
            return TypedResults.BadRequest(Problem("plan.planId is required."));
        }
        if (request.Plan.WorkItems is null || request.Plan.WorkItems.Count == 0)
        {
            return TypedResults.BadRequest(Problem("plan.workItems must contain at least one entry."));
        }
        if (request.Target is null)
        {
            return TypedResults.BadRequest(Problem("target is required."));
        }
        if (string.IsNullOrWhiteSpace(request.Target.Url) ||
            !Uri.TryCreate(request.Target.Url, UriKind.Absolute, out var uri) ||
            !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
        {
            return TypedResults.BadRequest(Problem("target.url must be an https://github.com/... URL."));
        }
        if (string.IsNullOrWhiteSpace(request.Target.CommitSha) ||
            request.Target.CommitSha.Length < 40 ||
            !request.Target.CommitSha.All(IsHex))
        {
            return TypedResults.BadRequest(Problem("target.commitSha must be a full 40+ char hex SHA."));
        }

        var job = store.Enqueue(request.Plan.PlanId, request.Target, request.Plan);
        var statusUrl = $"{http.Request.Scheme}://{http.Request.Host.Value}/api/migration-actions/jobs/{job.JobId}";
        var body = new MigrationActionsAccepted(job.JobId, job.Status.ToString().ToLowerInvariant(), statusUrl);
        return TypedResults.Accepted(statusUrl, body);
    }

    private static Results<Ok<MigrationActionsJobStatus>, NotFound<ProblemDetails>> HandleGetAsync(
        string jobId, MigrationJobStore store)
    {
        if (!store.TryGet(jobId, out var job))
        {
            return TypedResults.NotFound(Problem($"Unknown jobId '{jobId}'."));
        }
        return TypedResults.Ok(new MigrationActionsJobStatus(
            job.JobId,
            job.Status.ToString().ToLowerInvariant(),
            job.PlanId,
            job.Target,
            job.CreatedAt,
            job.StartedAt,
            job.FinishedAt,
            job.Result,
            job.Error));
    }

    private static bool IsHex(char c) =>
        (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

    // Proxy for the ARM64 runner status endpoint so the browser polls the composed host
    // (avoids CORS and keeps the runner bearer token server-side only).
    private static async Task<IResult> HandleArm64RunStatusAsync(
        string runId,
        [FromServices] Arm64BuildDispatcherOptions options,
        [FromServices] IHttpClientFactory httpClientFactory,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            return Results.NotFound(Problem("ARM64 runner is not configured."));
        }
        if (string.IsNullOrWhiteSpace(runId) || !runId.All(IsAlphanumeric))
        {
            return Results.BadRequest(Problem("runId must be alphanumeric."));
        }

        var client = httpClientFactory.CreateClient("arm64RunnerProxy");
        client.BaseAddress = new Uri(options.BaseUrl, UriKind.Absolute);
        client.Timeout = TimeSpan.FromSeconds(15);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/arm64/runs/{runId}");
        if (!string.IsNullOrWhiteSpace(options.BearerToken))
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", options.BearerToken);
        }
        try
        {
            using var response = await client.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return Results.Content(body, contentType: response.Content.Headers.ContentType?.ToString() ?? "application/json",
                statusCode: (int)response.StatusCode);
        }
        catch (HttpRequestException ex)
        {
            return Results.Problem($"ARM64 runner unreachable: {ex.Message}", statusCode: 502);
        }
    }

    private static bool IsAlphanumeric(char c) => char.IsLetterOrDigit(c);

    private static ProblemDetails Problem(string detail) => new()
    {
        Type = "https://tools.ietf.org/html/rfc9110#section-15.5.1",
        Title = "Bad Request",
        Status = 400,
        Detail = detail
    };
}
