using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MigrationPlanner.Api.Planning;

namespace MigrationPlanner.Api.Endpoints;

internal static class MigrationPlanRunsEndpoint
{
    public static IEndpointRouteBuilder MapMigrationPlanRunsEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/migration-plans/runs/{runId}", HandleAsync)
            .WithName("GetMigrationPlanRun");
        return endpoints;
    }

    private static IResult HandleAsync(string runId, PlanRunStore runStore)
    {
        if (!runStore.TryGet(runId, out var record))
        {
            return Results.NotFound(new { runId, status = "not-found" });
        }

        return record.Status switch
        {
            PlanRunStatus.Queued or PlanRunStatus.Running => Results.Ok(new
            {
                runId = record.RunId,
                status = record.Status.ToString().ToLowerInvariant(),
                createdAt = record.CreatedAt,
                startedAt = record.StartedAt,
            }),
            PlanRunStatus.Completed when record.Result is { IsSuccess: true } r => Results.Ok(new
            {
                runId = r.RunId,
                status = "completed",
                createdAt = record.CreatedAt,
                startedAt = record.StartedAt,
                finishedAt = record.FinishedAt,
                plan = r.Plan,
                score = r.Score,
                warnings = r.Warnings,
                automation = record.Automation,
            }),
            PlanRunStatus.Failed => BuildFailureResult(record),
            _ => Results.Ok(new
            {
                runId = record.RunId,
                status = record.Status.ToString().ToLowerInvariant(),
                createdAt = record.CreatedAt,
            }),
        };
    }

    private static IResult BuildFailureResult(PlanRunRecord record)
    {
        var response = new
        {
            runId = record.RunId,
            status = "failed",
            createdAt = record.CreatedAt,
            startedAt = record.StartedAt,
            finishedAt = record.FinishedAt,
            error = new
            {
                statusCode = record.FailureStatusCode,
                errorCode = record.FailureErrorCode,
                title = record.FailureTitle,
                errors = record.FailureErrors,
                retryAfterSeconds = record.RetryAfterSeconds,
            },
        };
        return Results.Ok(response);
    }
}
