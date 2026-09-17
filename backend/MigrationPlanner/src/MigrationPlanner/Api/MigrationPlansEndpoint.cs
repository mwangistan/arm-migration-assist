using System.Text.Json;
using MigrationPlanner.Assessment;
using MigrationPlanner.Llm;
using MigrationPlanner.Plan;
using MigrationPlanner.Scoring;

namespace MigrationPlanner.Api;

public static class MigrationPlansEndpoint
{
    private static readonly JsonSerializerOptions AssessmentJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static IEndpointRouteBuilder MapMigrationPlans(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/migration-plans", HandleAsync);
        return app;
    }

    private static async Task<IResult> HandleAsync(
        HttpContext ctx,
        DeterministicReadinessScorer scorer,
        PlannerOptions options,
        NarrativeFiller? filler,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        RepositoryAssessmentV1? assessment;
        try
        {
            assessment = await JsonSerializer.DeserializeAsync<RepositoryAssessmentV1>(
                ctx.Request.Body, AssessmentJson, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            return Results.Problem(title: "Invalid assessment body.", detail: ex.Message, statusCode: 400);
        }

        if (assessment is null || string.IsNullOrWhiteSpace(assessment.AssessmentId))
        {
            return Results.Problem(title: "Assessment body missing or empty.", statusCode: 400);
        }

        var runId = Guid.NewGuid().ToString("N");
        var t0 = DateTimeOffset.UtcNow;
        var score = scorer.Score(assessment);

        var provenance = new ModelProvenance
        {
            Provider = options.ProvenanceProvider,
            Name = options.ProvenanceName,
            Version = options.ProvenanceVersion,
        };

        var skeleton = PlanSkeletonBuilder.Build(
            assessment, score, provenance,
            planId: $"plan-{runId[..12]}",
            generatedAt: DateTimeOffset.UtcNow,
            corpusVersion: options.CorpusVersion);

        var skeletonMs = (int)(DateTimeOffset.UtcNow - t0).TotalMilliseconds;
        var warnings = new List<string>();
        var plan = skeleton;

        if (options.SkeletonOnly || filler is null)
        {
            warnings.Add("Skeleton-only mode: narrative fields are placeholders.");
        }
        else
        {
            var tNarrative = DateTimeOffset.UtcNow;
            plan = await filler.FillAsync(skeleton, assessment, score, cancellationToken).ConfigureAwait(false);
            var narrativeMs = (int)(DateTimeOffset.UtcNow - tNarrative).TotalMilliseconds;
            logger.LogInformation("narrative_completed runId={RunId} narrativeMs={Narrative}", runId, narrativeMs);
        }

        var totalMs = (int)(DateTimeOffset.UtcNow - t0).TotalMilliseconds;
        logger.LogInformation(
            "plan_generated runId={RunId} assessmentId={AssessmentId} band={Band} path={Path} workItems={WorkItems} skeletonMs={SkeletonMs} totalMs={TotalMs}",
            runId, assessment.AssessmentId, score.Band, plan.RecommendedPath, plan.WorkItems.Count, skeletonMs, totalMs);

        return Results.Ok(new
        {
            runId,
            plan,
            score,
            warnings = warnings.ToArray(),
        });
    }
}
