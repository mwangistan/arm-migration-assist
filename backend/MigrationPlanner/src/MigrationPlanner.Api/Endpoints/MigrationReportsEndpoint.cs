using MigrationPlanner.Application.Reporting;

namespace MigrationPlanner.Api.Endpoints;

internal static class MigrationReportsEndpoint
{
    public static IEndpointRouteBuilder MapMigrationReportsEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/migration-plans/{runId}/report.{extension}", HandleAsync)
            .WithName("DownloadMigrationReport");
        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        string runId,
        string extension,
        IPlanArtifactStore artifactStore,
        IEnumerable<IMigrationReportRenderer> renderers,
        CancellationToken cancellationToken)
    {
        var renderer = renderers.SingleOrDefault(candidate =>
            candidate.FileExtension.Equals(extension, StringComparison.OrdinalIgnoreCase));
        if (renderer is null)
        {
            return PlannerProblemDetailsFactory.Problem(
                StatusCodes.Status415UnsupportedMediaType,
                "report-format-unsupported",
                "The requested report format is not supported.",
                ["Use .md or .html."]);
        }

        var report = artifactStore.Get(runId);
        if (report is null)
        {
            return PlannerProblemDetailsFactory.Problem(
                StatusCodes.Status404NotFound,
                "planning-run-not-found",
                "The planning run was not found.",
                ["The run may have expired or belongs to another service instance."]);
        }

        var stream = await renderer.RenderAsync(report, cancellationToken);
        return Results.File(
            stream,
            renderer.ContentType,
            $"migration-report-{runId}.{renderer.FileExtension}");
    }
}
