using ArmMigrationAssist.Api.Assessment.RepositoryDiscovery;

namespace ArmMigrationAssist.Api.Assessment;

/// <summary>
/// Feature 1 orchestrator. Runs Story 1.1 ingestion, then invokes every registered
/// <see cref="IAssessmentSkill"/> (Stories 1.2 - 1.4 + build readiness) in order and
/// assembles the evidence-based <see cref="ReadinessManifest"/> - the contract Feature 2
/// consumes. Deterministic scanners only (no AI), per the plan's "deterministic before
/// generative" principle.
/// </summary>
public sealed class AssessmentService
{
    private readonly RepositoryIngestionService _ingestion;
    private readonly IReadOnlyList<IAssessmentSkill> _skills;
    private readonly ILogger<AssessmentService> _logger;

    public AssessmentService(
        RepositoryIngestionService ingestion,
        IEnumerable<IAssessmentSkill> skills,
        ILogger<AssessmentService> logger)
    {
        _ingestion = ingestion;
        _skills = skills.OrderBy(s => s.Order).ToList();
        _logger = logger;
    }

    public async Task<ReadinessManifest> AssessAsync(AssessmentRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Assessing {RepoUrl} for target {Target}", request.RepoUrl, request.Target);

        var snapshot = await _ingestion.IngestAsync(request.RepoUrl, ct);   // Story 1.1
        var manifest = new ReadinessManifest { Repository = snapshot, Target = request.Target };
        manifest.FilesScanned = snapshot.FileCount;

        foreach (var skill in _skills)                                      // Stories 1.2 - 1.4 + build/windows
        {
            ct.ThrowIfCancellationRequested();
            _logger.LogInformation("Running skill {Skill}", skill.Name);

            manifest.Skills.Add(new SkillInfo(
                skill.Name, "1.0.0",
                skill.Description, WriteAccess: false,
                SupportedInputs: ["repository-snapshot"],
                SupportedOutputs: skill.Outputs));

            try
            {
                await skill.ContributeAsync(snapshot, manifest, ct);
                manifest.ScannersCompleted.Add(skill.Name);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Skill {Skill} failed", skill.Name);
                manifest.ScannersFailed.Add(skill.Name);
            }
        }

        return manifest;
    }
}
