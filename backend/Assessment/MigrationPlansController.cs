using System.Text.Json;
using ArmMigrationAssist.Api.MigrationPlanner;
using Microsoft.AspNetCore.Mvc;

namespace ArmMigrationAssist.Api.Controllers;

[ApiController]
[Route("api/migration-plans")]
public sealed class MigrationPlansController(
    MigrationPlannerClient planner,
    ILogger<MigrationPlansController> logger) : ControllerBase
{
    [HttpPost]
    [Consumes("application/json")]
    public async Task<IActionResult> Post(
        [FromBody] JsonElement assessment,
        CancellationToken cancellationToken)
    {
        if (assessment.ValueKind != JsonValueKind.Object)
            return BadRequest(new { error = "A RepositoryAssessmentV1 JSON object is required." });

        try
        {
            var response = await planner.CreatePlanAsync(assessment, cancellationToken);
            return new ContentResult
            {
                StatusCode = response.StatusCode,
                Content = response.Content,
                ContentType = response.ContentType
            };
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "The migration planner timed out.");
            return Problem(
                statusCode: StatusCodes.Status504GatewayTimeout,
                title: "The migration planner timed out.",
                detail: "The planner did not return a response within ten minutes.");
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "The migration planner is unavailable.");
            return Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "The migration planner is unavailable.",
                detail: "The assessment completed, but the planning service could not be reached.");
        }
    }
}
