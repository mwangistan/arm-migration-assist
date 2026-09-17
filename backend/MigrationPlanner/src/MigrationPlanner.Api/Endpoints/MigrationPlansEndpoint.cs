using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
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
        MigrationPlanningService planningService,
        IPlanArtifactStore artifactStore,
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

            PlanResult result;
            try
            {
                result = await planningService
                    .PlanAsync(new PlanRequest(assessment), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (CorpusIntegrityException ex)
            {
                return PlannerProblemDetailsFactory.Problem(
                    StatusCodes.Status503ServiceUnavailable,
                    PlannerErrorCode.CorpusIntegrity,
                    "Guidance corpus integrity check failed.",
                    [ex.Message]);
            }

            if (!result.IsSuccess)
            {
                if (result.ErrorCode == PlannerErrorCode.ModelRateLimited)
                {
                    var seconds = (int)Math.Ceiling((result.RetryAfter ?? TimeSpan.FromSeconds(30)).TotalSeconds);
                    context.Response.Headers.Append("Retry-After", seconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    return PlannerProblemDetailsFactory.Problem(
                        StatusCodes.Status429TooManyRequests,
                        PlannerErrorCode.ModelRateLimited,
                        $"Rate limit reached upstream. Retry in {seconds} seconds.",
                        result.Errors ?? Array.Empty<string>());
                }

                var statusCode = MapErrorStatus(result.ErrorCode);
                return PlannerProblemDetailsFactory.Problem(
                    statusCode,
                    result.ErrorCode ?? PlannerErrorCode.Internal,
                    "Migration plan could not be produced.",
                    result.Errors ?? Array.Empty<string>());
            }

            artifactStore.Store(MigrationReportFactory.From(assessment, result));
            return Results.Ok(new
            {
                runId = result.RunId,
                plan = result.Plan,
                score = result.Score,
                warnings = result.Warnings,
            });
        }
    }

    private static int MapErrorStatus(string? errorCode) => errorCode switch
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
        PlannerErrorCode.PlanUnderGranular => StatusCodes.Status422UnprocessableEntity,
        _ => StatusCodes.Status500InternalServerError,
    };
}
