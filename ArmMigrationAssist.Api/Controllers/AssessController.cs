using ArmMigrationAssist.Api.Assessment;
using ArmMigrationAssist.Api.Assessment.Contract;
using Microsoft.AspNetCore.Mvc;

namespace ArmMigrationAssist.Api.Controllers;

/// <summary>Feature 1 - Repository Assessment Engine entry point.</summary>
[ApiController]
[Route("[controller]")]
public sealed class AssessController : ControllerBase
{
    private readonly AssessmentService _assessment;

    public AssessController(AssessmentService assessment) => _assessment = assessment;

    /// <summary>
    /// POST /assess - ingest a public GitHub repository and return a RepositoryAssessmentV1
    /// document (the contract consumed by the Feature 2 planner), Stories 1.1 - 1.4.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<RepositoryAssessmentV1>> Post(
        [FromBody] AssessmentRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.RepoUrl))
            return BadRequest("repoUrl is required.");

        try
        {
            var manifest = await _assessment.AssessAsync(request, ct);
            var doc = AssessmentV1Mapper.Map(manifest);

            var errors = EvidenceValidator.Validate(doc);
            if (errors.Count > 0)
                return UnprocessableEntity(new { error = "Evidence validation failed.", details = errors });

            return Ok(doc);
        }
        catch (InvalidOperationException ex)
        {
            return UnprocessableEntity(new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
