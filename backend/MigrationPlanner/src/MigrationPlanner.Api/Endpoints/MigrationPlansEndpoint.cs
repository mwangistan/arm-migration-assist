using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using MigrationPlanner.Api.Automation;
using MigrationPlanner.Api.Planning;
using MigrationPlanner.Application.Abstractions;
using MigrationPlanner.Application.Planning;
using MigrationPlanner.Application.Reporting;
using MigrationPlanner.Domain.Assessment;
using MigrationPlanner.Domain.Errors;
using MigrationPlanner.Infrastructure.Guidance;

namespace MigrationPlanner.Api.Endpoints;

internal static class MigrationPlansEndpoint
{
    private static readonly JsonSerializerOptions DeserializeOptions = new()
    {
        PropertyNamingPolicy = null,
    };

    public static IEndpointRouteBuilder MapMigrationPlansEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/migration-plans", HandleAsync)
            .WithName("CreateMigrationPlan");
        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        IAssessmentSchemaValidator schemaValidator,
        PlanRunStore runStore,
        IServiceScopeFactory scopeFactory,
        CancellationToken cancellationToken)
    {
        JsonDocument document;
        try
        {
            document = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            return PlannerProblemDetailsFactory.Problem(
                StatusCodes.Status400BadRequest,
                PlannerErrorCode.SchemaInvalid,
                "Request body is not valid JSON.",
                [ex.Message]);
        }

        using (document)
        {
            var schemaResult = schemaValidator.Validate(document.RootElement);
            if (!schemaResult.IsValid)
            {
                var statusCode = schemaResult.ErrorCode == PlannerErrorCode.SchemaUnsupportedVersion
                    ? StatusCodes.Status409Conflict
                    : StatusCodes.Status400BadRequest;
                return PlannerProblemDetailsFactory.Problem(
                    statusCode,
                    schemaResult.ErrorCode ?? PlannerErrorCode.SchemaInvalid,
                    "Repository assessment failed schema validation.",
                    schemaResult.Errors);
            }

            RepositoryAssessmentV1? assessment;
            try
            {
                assessment = document.RootElement.Deserialize<RepositoryAssessmentV1>(DeserializeOptions);
            }
            catch (JsonException ex)
            {
                return PlannerProblemDetailsFactory.Problem(
                    StatusCodes.Status400BadRequest,
                    PlannerErrorCode.SchemaInvalid,
                    "Repository assessment could not be bound to RepositoryAssessmentV1.",
                    [ex.Message]);
            }

            if (assessment is null)
            {
                return PlannerProblemDetailsFactory.Problem(
                    StatusCodes.Status400BadRequest,
                    PlannerErrorCode.SchemaInvalid,
                    "Repository assessment payload was null.",
                    Array.Empty<string>());
            }

            // Fork to a background task so the caller does not block on model generation
            // (which frequently exceeds SWA's 45s ingress timeout). Frontend polls the
            // returned statusUrl for completion.
            var run = runStore.Create();
            _ = Task.Run(() => ExecutePlanAsync(run, assessment, scopeFactory));

            var statusUrl = $"/api/migration-plans/runs/{run.RunId}";
            var envelope = new
            {
                runId = run.RunId,
                status = "queued",
                statusUrl,
                createdAt = run.CreatedAt,
            };
            return Results.Accepted(statusUrl, envelope);
        }
    }

    private static async Task ExecutePlanAsync(
        PlanRunRecord run,
        RepositoryAssessmentV1 assessment,
        IServiceScopeFactory scopeFactory)
    {
        run.StartedAt = DateTimeOffset.UtcNow;
        run.Status = PlanRunStatus.Running;

        // Detached from the caller's cancellation token; the request already returned.
        var cancellationToken = CancellationToken.None;

        try
        {
            using var scope = scopeFactory.CreateScope();
            var planningService = scope.ServiceProvider.GetRequiredService<MigrationPlanningService>();
            var artifactStore = scope.ServiceProvider.GetRequiredService<IPlanArtifactStore>();
            var automationDispatcher = scope.ServiceProvider.GetRequiredService<IAutomationDispatcher>();

            PlanResult result;
            try
            {
                result = await planningService
                    .PlanAsync(new PlanRequest(assessment), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (CorpusIntegrityException ex)
            {
                run.Status = PlanRunStatus.Failed;
                run.FailureStatusCode = StatusCodes.Status503ServiceUnavailable;
                run.FailureErrorCode = PlannerErrorCode.CorpusIntegrity;
                run.FailureTitle = "Guidance corpus integrity check failed.";
                run.FailureErrors = new[] { ex.Message };
                return;
            }

            if (!result.IsSuccess)
            {
                run.Status = PlanRunStatus.Failed;
                if (result.ErrorCode == PlannerErrorCode.ModelRateLimited)
                {
                    run.RetryAfterSeconds = (int)Math.Ceiling((result.RetryAfter ?? TimeSpan.FromSeconds(30)).TotalSeconds);
                    run.FailureStatusCode = StatusCodes.Status429TooManyRequests;
                    run.FailureErrorCode = PlannerErrorCode.ModelRateLimited;
                    run.FailureTitle = $"Rate limit reached upstream. Retry in {run.RetryAfterSeconds} seconds.";
                }
                else
                {
                    run.FailureStatusCode = MapErrorStatus(result.ErrorCode);
                    run.FailureErrorCode = result.ErrorCode ?? PlannerErrorCode.Internal;
                    run.FailureTitle = "Migration plan could not be produced.";
                }
                run.FailureErrors = result.Errors ?? Array.Empty<string>();
                return;
            }

            artifactStore.Store(MigrationReportFactory.From(assessment, result));

            var automation = await automationDispatcher
                .DispatchAsync(result.Plan!, assessment.Repository, cancellationToken)
                .ConfigureAwait(false);

            run.Result = result;
            run.Automation = automation;
            run.Status = PlanRunStatus.Completed;
        }
        catch (Exception ex)
        {
            run.Status = PlanRunStatus.Failed;
            run.FailureStatusCode = StatusCodes.Status500InternalServerError;
            run.FailureErrorCode = PlannerErrorCode.Internal;
            run.FailureTitle = "Migration planning failed unexpectedly.";
            run.FailureErrors = new[] { ex.Message };
        }
        finally
        {
            run.FinishedAt = DateTimeOffset.UtcNow;
        }
    }

    internal static int MapErrorStatus(string? errorCode) => errorCode switch
    {
        PlannerErrorCode.SchemaInvalid => StatusCodes.Status400BadRequest,
        PlannerErrorCode.SchemaUnsupportedVersion => StatusCodes.Status409Conflict,
        PlannerErrorCode.EvidenceInvalid => StatusCodes.Status400BadRequest,
        PlannerErrorCode.EvidenceDuplicateId => StatusCodes.Status400BadRequest,
        PlannerErrorCode.ScannerCoverageInvalid => StatusCodes.Status400BadRequest,
        PlannerErrorCode.McpToolUnknown => StatusCodes.Status400BadRequest,
        PlannerErrorCode.CorpusIntegrity => StatusCodes.Status503ServiceUnavailable,
        PlannerErrorCode.ModelFailed => StatusCodes.Status502BadGateway,
        PlannerErrorCode.ModelOutputInvalid => StatusCodes.Status502BadGateway,
        PlannerErrorCode.PlanSafetyViolation => StatusCodes.Status422UnprocessableEntity,
        PlannerErrorCode.PlanShapeInvalid => StatusCodes.Status422UnprocessableEntity,
        PlannerErrorCode.PlanScoreDigestMismatch => StatusCodes.Status422UnprocessableEntity,
        PlannerErrorCode.PlanRecommendationInconsistent => StatusCodes.Status422UnprocessableEntity,
        PlannerErrorCode.PlanEvidenceMissing => StatusCodes.Status422UnprocessableEntity,
        PlannerErrorCode.PlanGuidanceMissing => StatusCodes.Status422UnprocessableEntity,
        PlannerErrorCode.PlanMissingSkill => StatusCodes.Status422UnprocessableEntity,
        PlannerErrorCode.PlanSkillIoMismatch => StatusCodes.Status422UnprocessableEntity,
        PlannerErrorCode.PlanApprovalMissing => StatusCodes.Status422UnprocessableEntity,
        PlannerErrorCode.PlanUnderGranular => StatusCodes.Status422UnprocessableEntity,
        _ => StatusCodes.Status500InternalServerError,
    };
}