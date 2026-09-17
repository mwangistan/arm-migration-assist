using System.Text.Json;
using MigrationPlanner.Application.Abstractions;
using MigrationPlanner.Application.Planning;
using MigrationPlanner.Domain.Assessment;
using MigrationPlanner.Domain.Plan;

namespace MigrationPlanner.Infrastructure.Model;

/// <summary>
/// Deterministic canned model. Emits a `MigrationPlanV1`-shaped JSON string
/// whose `assessmentId` echoes the request and whose `recommendedPath` /
/// `confidence` are derived from <see cref="ReadinessScoreV1"/>. No cloud
/// dependencies. Selected when <c>MIGRATIONPLANNER_MODEL_PROVIDER</c> is
/// unset or set to <c>Fake</c>.
/// </summary>
public sealed class FakePlannerModel : IPlannerModel
{
    private const int MaxCitedEvidenceIds = 5;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = false,
    };

    public Task<PlannerModelResult> GeneratePlanJsonAsync(
        RepositoryAssessmentV1 assessment,
        ReadinessScoreV1 score,
        IGuidanceLookup guidanceLookup,
        CancellationToken cancellationToken,
        PlannerRetryHint? retryHint = null)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        ArgumentNullException.ThrowIfNull(score);
        ArgumentNullException.ThrowIfNull(guidanceLookup);
        cancellationToken.ThrowIfCancellationRequested();
        _ = retryHint; // fake output is always dispatch-consistent; hint is a no-op.

        var (recommendedPath, confidence) = MapRecommendation(score);
        var citedEvidence = score.EvidenceIds.Take(MaxCitedEvidenceIds).ToArray();

        var payload = new
        {
            schemaVersion = "1.0",
            planId = $"plan-{assessment.AssessmentId}",
            assessmentId = assessment.AssessmentId,
            generatedAt = DateTimeOffset.UtcNow.ToString("O"),
            modelProvenance = new { provider = "fake", name = "fake-planner", version = "0.1.0" },
            scoreDigest = ScoreDigest.Compute(score),
            corpusVersion = guidanceLookup.CorpusVersion,
            recommendedPath,
            confidence,
            executiveSummary = BuildExecutiveSummary(score),
            scoreInterpretation = BuildScoreInterpretation(score),
            facts = BuildFacts(score, citedEvidence),
            inferences = Array.Empty<object>(),
            alternatives = new[]
            {
                new
                {
                    path = recommendedPath,
                    disposition = "viable",
                    rationale = $"Deterministic score band '{EnumToWire(score.Band)}' (overall {score.OverallScore}) drives this fake-provider recommendation.",
                    evidenceIds = citedEvidence,
                    guidanceIds = Array.Empty<string>(),
                },
            },
            workItems = BuildWorkItems(assessment, score, recommendedPath),
            missingSkills = BuildMissingSkills(assessment, score, recommendedPath),
            validationPlan = new
            {
                targetDevices = new[] { "arm64-vm" },
                buildChecks = Array.Empty<object>(),
                functionalChecks = Array.Empty<object>(),
                reliabilityChecks = Array.Empty<object>(),
                performanceChecks = Array.Empty<object>(),
                powerChecks = Array.Empty<object>(),
                offlineChecks = Array.Empty<object>(),
                accessibilityChecks = Array.Empty<object>(),
                windowsExperienceChecks = Array.Empty<object>(),
            },
            risks = Array.Empty<object>(),
            unknowns = Array.Empty<object>(),
            requiredApprovals = Array.Empty<object>(),
            reusableOutputs = Array.Empty<object>(),
        };

        var json = JsonSerializer.Serialize(payload, SerializerOptions);
        return Task.FromResult(PlannerModelResult.FromJson(json));
    }

    private static (string Path, string Confidence) MapRecommendation(ReadinessScoreV1 score) =>
        RecommendationDispatch.Choose(score);

    private static string BuildExecutiveSummary(ReadinessScoreV1 score)
    {
        var band = EnumToWire(score.Band);
        var capsPart = score.CapsApplied.Count == 0
            ? "No blocker caps fired."
            : $"Cap(s) applied: {string.Join(", ", score.CapsApplied.Select(c => EnumToWire(c.CapId)))}.";
        var provisionalPart = score.Provisional
            ? $"Assessment flagged provisional ({string.Join(", ", score.ProvisionalReasons.Select(EnumToWire))})."
            : "Assessment coverage is sufficient for a full recommendation.";
        return $"Deterministic band '{band}' at overall {score.OverallScore}/100 (uncapped {score.UncappedScore}). "
            + $"{capsPart} {provisionalPart} This is a fake-provider placeholder summary.";
    }

    private static string BuildScoreInterpretation(ReadinessScoreV1 score)
    {
        var lines = new List<string>();
        foreach (var d in score.Dimensions)
        {
            lines.Add($"{EnumToWire(d.DimensionKey)}: raw {d.RawScore} × {d.WeightPct}% = {d.WeightedContribution:0.##}");
        }
        return $"Dimension breakdown ({string.Join("; ", lines)}). Evidence completeness {score.EvidenceCompletenessScore:0.00} ({EnumToWire(score.EvidenceCompleteness)}).";
    }

    private static object[] BuildFacts(ReadinessScoreV1 score, IReadOnlyList<string> citedEvidence)
    {
        if (citedEvidence.Count == 0)
        {
            return Array.Empty<object>();
        }
        return new object[]
        {
            new
            {
                statement = $"Deterministic scorer observed {score.EvidenceIds.Count} evidence record(s) across five dimensions.",
                evidenceIds = citedEvidence,
                guidanceIds = Array.Empty<string>(),
            },
        };
    }

    private static object[] BuildWorkItems(
        RepositoryAssessmentV1 assessment, ReadinessScoreV1 score, string recommendedPath)
    {
        if (string.Equals(recommendedPath, "insufficient-evidence", StringComparison.Ordinal))
        {
            return Array.Empty<object>();
        }

        var expectation = GranularityCalculator.Compute(assessment, score);
        var items = new List<object>();
        string? buildWorkItemId = null;
        var seq = 1;
        foreach (var bucket in expectation.Buckets)
        {
            var slug = SlugifyForId(bucket.Description);
            var id = $"wi-{bucket.Category}-{slug}";
            if (id.Length > 60) id = id[..60].TrimEnd('-');
            var skill = ResolveSkill(bucket);
            var dependencies = skill == "ci-pipeline-generator" && buildWorkItemId is not null
                ? new[] { buildWorkItemId }
                : Array.Empty<string>();
            var inputs = ResolveInputs(assessment, bucket, skill);
            items.Add(new
            {
                id,
                sequence = seq++,
                priority = bucket.Category == "dep" ? "P1" : "P0",
                title = bucket.Description,
                objective = bucket.Description + " Produce the concrete change and validate on ARM64.",
                agentOrSkill = skill,
                inputs,
                expectedOutputs = new[] { "patch" },
                dependencies,
                evidenceIds = bucket.EvidenceIds,
                guidanceIds = Array.Empty<string>(),
                acceptanceTests = new[]
                {
                    new
                    {
                        id = $"at-{bucket.Category}-{seq}-builds",
                        description = "Change builds and integrates with existing pipelines.",
                        expectedOutcome = "Green build on ARM64.",
                    },
                    new
                    {
                        id = $"at-{bucket.Category}-{seq}-functional",
                        description = "Functional smoke test passes on ARM64.",
                        expectedOutcome = "No crash or regression against the x64 baseline.",
                    },
                },
                approvalRequired = true,
                estimatedEffort = bucket.Category == "dep" ? "large" : "medium",
                risk = bucket.Category == "dep" ? "high" : "low",
            });
            if (skill == "build-config-generator")
            {
                buildWorkItemId = id;
            }
        }
        return items.ToArray();
    }

    private static object[] BuildMissingSkills(
        RepositoryAssessmentV1 assessment, ReadinessScoreV1 score, string recommendedPath)
    {
        if (string.Equals(recommendedPath, "insufficient-evidence", StringComparison.Ordinal))
        {
            return Array.Empty<object>();
        }

        var expectation = GranularityCalculator.Compute(assessment, score);
        var available = new HashSet<string>(
            assessment.AvailableSkills.Select(s => s.Name),
            StringComparer.Ordinal);
        var declared = new HashSet<string>(StringComparer.Ordinal);
        var missing = new List<object>();

        foreach (var bucket in expectation.Buckets)
        {
            var skill = ResolveSkill(bucket);
            if (available.Contains(skill) || !declared.Add(skill)) continue;
            missing.Add(new
            {
                proposedName = skill,
                purpose = $"Migration capability required for '{bucket.Description}'",
                requiredInputs = new[] { "migration-plan-v1", "repository-workspace" },
                expectedOutputs = new[] { "patch" },
                justification = "The required migration generator is not present in the assessment skill catalog.",
                evidenceIds = bucket.EvidenceIds,
                writeAccess = true,
            });
        }
        return missing.ToArray();
    }

    private static string ResolveSkill(GranularityCalculator.ExpectedBucket bucket)
    {
        if (bucket.Description.Contains("CI job", StringComparison.OrdinalIgnoreCase))
        {
            return "ci-pipeline-generator";
        }

        return bucket.Category == "build"
            && !bucket.Description.Contains("test suite", StringComparison.OrdinalIgnoreCase)
                ? "build-config-generator"
                : "code-transformer";
    }

    private static string[] ResolveInputs(
        RepositoryAssessmentV1 assessment,
        GranularityCalculator.ExpectedBucket bucket,
        string skill)
    {
        IEnumerable<string?> paths = bucket.Category switch
        {
            "code" => assessment.CodeFindings
                .Where(finding => bucket.EvidenceIds.Contains(finding.EvidenceId, StringComparer.Ordinal))
                .Select(finding => finding.File),
            "dep" => assessment.Dependencies
                .Where(dependency => bucket.EvidenceIds.Contains(dependency.EvidenceId, StringComparer.Ordinal))
                .SelectMany(dependency => dependency.Evidence.Select(evidence => evidence.Path)),
            _ => assessment.BuildFindings.Evidence.Select(evidence => evidence.Path),
        };

        if (skill == "build-config-generator")
        {
            paths = paths.Where(path => path is not null && IsSupportedBuildInput(path));
        }

        return paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!)
            .Distinct(StringComparer.Ordinal)
            .Take(8)
            .ToArray();
    }

    private static bool IsSupportedBuildInput(string path)
    {
        var name = Path.GetFileName(path);
        return name.Equals("Dockerfile", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".vcxproj", StringComparison.OrdinalIgnoreCase);
    }

    private static string SlugifyForId(string text)
    {
        var chars = text.ToLowerInvariant().ToCharArray();
        var sb = new System.Text.StringBuilder(chars.Length);
        var lastDash = true;
        foreach (var c in chars)
        {
            if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
            {
                sb.Append(c);
                lastDash = false;
            }
            else if (!lastDash && sb.Length > 0)
            {
                sb.Append('-');
                lastDash = true;
            }
        }
        var slug = sb.ToString().Trim('-');
        return string.IsNullOrEmpty(slug) ? "item" : slug;
    }

    private static string EnumToWire<TEnum>(TEnum value) where TEnum : struct, Enum
    {
        var member = typeof(TEnum).GetField(value.ToString(),
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        var attr = member?.GetCustomAttributes(typeof(System.Runtime.Serialization.EnumMemberAttribute), false)
            .Cast<System.Runtime.Serialization.EnumMemberAttribute>()
            .FirstOrDefault();
        return attr?.Value ?? value.ToString();
    }
}
