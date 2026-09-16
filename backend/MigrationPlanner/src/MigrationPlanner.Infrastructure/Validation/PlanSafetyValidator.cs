using System.Text.Json;
using MigrationPlanner.Application.Abstractions;
using MigrationPlanner.Domain.Assessment;
using MigrationPlanner.Domain.Errors;
using MigrationPlanner.Domain.Plan;
using MigrationPlanner.Infrastructure.Model;

namespace MigrationPlanner.Infrastructure.Validation;

/// <summary>
/// Post-model validator. Enforces top-level shape, unsafe-instruction
/// scanning, byte-identical <c>scoreDigest</c> match with the deterministic
/// score, and resolution of every <c>evidenceId</c> / <c>guidanceId</c>
/// citation in the plan.
/// </summary>
public sealed class PlanSafetyValidator : IPlanSafetyValidator
{
    // Authoritative top-level property allowlist for MigrationPlanV1.
    private static readonly HashSet<string> AllowedTopLevelKeys = new(StringComparer.Ordinal)
    {
        "schemaVersion",
        "planId",
        "assessmentId",
        "generatedAt",
        "modelProvenance",
        "scoreDigest",
        "corpusVersion",
        "recommendedPath",
        "confidence",
        "executiveSummary",
        "scoreInterpretation",
        "facts",
        "inferences",
        "alternatives",
        "workItems",
        "missingSkills",
        "validationPlan",
        "risks",
        "unknowns",
        "requiredApprovals",
        "reusableOutputs",
    };

    private static readonly string[] RequiredTopLevelKeys =
    [
        "schemaVersion",
        "planId",
        "assessmentId",
        "generatedAt",
        "modelProvenance",
        "scoreDigest",
        "corpusVersion",
        "recommendedPath",
        "confidence",
        "executiveSummary",
        "scoreInterpretation",
        "facts",
        "inferences",
        "alternatives",
        "workItems",
        "missingSkills",
        "validationPlan",
        "risks",
        "unknowns",
        "requiredApprovals",
        "reusableOutputs",
    ];

    private static readonly string[] UnsafePhrases =
    [
        "git push",
        "git commit",
        "git reset",
        "rm -rf",
        "az login",
        "open pull request",
        "create pull request",
        "publish nuget",
        "publish to",
        "run shell",
        "sudo ",
    ];

    private readonly IWindowsOnArmGuidanceStore _guidanceStore;

    public PlanSafetyValidator(IWindowsOnArmGuidanceStore guidanceStore)
    {
        _guidanceStore = guidanceStore;
    }

    public PlanSafetyResult Validate(MigrationPlanV1 plan, RepositoryAssessmentV1 assessment, ReadinessScoreV1 score)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(assessment);
        ArgumentNullException.ThrowIfNull(score);

        if (plan.AssessmentId != assessment.AssessmentId)
        {
            return PlanSafetyResult.Fail(PlannerErrorCode.PlanSafetyViolation,
                "Plan assessmentId does not match request.");
        }

        var shapeViolations = ValidateTopLevelShape(plan);
        if (shapeViolations.Count > 0)
        {
            return PlanSafetyResult.Fail(PlannerErrorCode.PlanShapeInvalid, shapeViolations.ToArray());
        }

        var digestViolations = ValidateScoreDigest(plan, score);
        if (digestViolations.Count > 0)
        {
            return PlanSafetyResult.Fail(PlannerErrorCode.PlanScoreDigestMismatch, digestViolations.ToArray());
        }

        var recommendationViolations = ValidateRecommendation(plan, score);
        if (recommendationViolations.Count > 0)
        {
            return PlanSafetyResult.Fail(
                PlannerErrorCode.PlanRecommendationInconsistent, recommendationViolations.ToArray());
        }

        var unsafeViolations = new List<string>();
        if (plan.AdditionalProperties is { Count: > 0 })
        {
            foreach (var kv in plan.AdditionalProperties)
            {
                CheckElementForUnsafeText(kv.Key, kv.Value, unsafeViolations);
            }
        }

        if (unsafeViolations.Count > 0)
        {
            return PlanSafetyResult.Fail(PlannerErrorCode.PlanSafetyViolation, unsafeViolations.ToArray());
        }

        var evidenceViolations = ValidateEvidenceCitations(plan, assessment);
        if (evidenceViolations.Count > 0)
        {
            return PlanSafetyResult.Fail(PlannerErrorCode.PlanEvidenceMissing, evidenceViolations.ToArray());
        }

        var guidanceViolations = ValidateGuidanceCitations(plan);
        if (guidanceViolations.Count > 0)
        {
            return PlanSafetyResult.Fail(PlannerErrorCode.PlanGuidanceMissing, guidanceViolations.ToArray());
        }

        return PlanSafetyResult.Ok;
    }

    private static List<string> ValidateTopLevelShape(MigrationPlanV1 plan)
    {
        var violations = new List<string>();
        var present = new HashSet<string>(StringComparer.Ordinal) { "schemaVersion", "assessmentId" };

        if (plan.AdditionalProperties is { Count: > 0 })
        {
            foreach (var key in plan.AdditionalProperties.Keys)
            {
                if (!AllowedTopLevelKeys.Contains(key))
                {
                    violations.Add($"Plan contains unknown top-level property '{key}'.");
                }
                else
                {
                    present.Add(key);
                }
            }
        }

        foreach (var required in RequiredTopLevelKeys)
        {
            if (!present.Contains(required))
            {
                violations.Add($"Plan is missing required top-level property '{required}'.");
            }
        }

        return violations;
    }

    private static List<string> ValidateScoreDigest(MigrationPlanV1 plan, ReadinessScoreV1 score)
    {
        var violations = new List<string>();
        var expected = ScoreDigest.Compute(score);

        if (plan.AdditionalProperties is null
            || !plan.AdditionalProperties.TryGetValue("scoreDigest", out var element)
            || element.ValueKind != JsonValueKind.String)
        {
            violations.Add("Plan is missing a string scoreDigest.");
            return violations;
        }

        var actual = element.GetString();
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            violations.Add(
                $"Plan scoreDigest '{actual}' does not match deterministic score digest '{expected}'.");
        }

        return violations;
    }

    private static List<string> ValidateRecommendation(MigrationPlanV1 plan, ReadinessScoreV1 score)
    {
        var violations = new List<string>();
        var (expectedPath, _) = RecommendationDispatch.Choose(score);

        if (plan.AdditionalProperties is null
            || !plan.AdditionalProperties.TryGetValue("recommendedPath", out var pathElement)
            || pathElement.ValueKind != JsonValueKind.String)
        {
            violations.Add("Plan is missing a string recommendedPath.");
            return violations;
        }

        var actualPath = pathElement.GetString();
        if (!string.Equals(actualPath, expectedPath, StringComparison.Ordinal))
        {
            violations.Add(
                $"Plan recommendedPath '{actualPath}' does not match the deterministic dispatch '{expectedPath}' "
                + $"(band={ScoreBand(score)}, provisional={score.Provisional}, "
                + $"capsApplied=[{string.Join(",", score.CapsApplied.Select(c => CapIdWire(c.CapId)))}]).");
        }

        return violations;
    }

    private static string ScoreBand(ReadinessScoreV1 score) => score.Band switch
    {
        ReadinessBand.ReadyOrMinorChanges => "ready-or-minor-changes",
        ReadinessBand.ModerateMigration => "moderate-migration",
        ReadinessBand.SignificantRemediation => "significant-remediation",
        ReadinessBand.BlockedOrMajorRedesign => "blocked-or-major-redesign",
        _ => "insufficient-evidence",
    };

    private static string CapIdWire(CapId capId) => capId switch
    {
        CapId.RequiredUnsupportedDriverLe30 => "required-unsupported-driver-le-30",
        CapId.RequiredX64OnlyNativeLe40 => "required-x64-only-native-le-40",
        CapId.NoArm64OrArm64EcTargetLe60 => "no-arm64-or-arm64ec-target-le-60",
        _ => capId.ToString(),
    };

    private static List<string> ValidateEvidenceCitations(MigrationPlanV1 plan, RepositoryAssessmentV1 assessment)
    {
        var violations = new List<string>();
        var allowed = BuildAssessmentEvidenceIds(assessment);

        foreach (var (owner, evidenceId) in CollectStringArray(plan, "evidenceIds"))
        {
            if (!allowed.Contains(evidenceId))
            {
                violations.Add($"Plan cites unknown evidenceId '{evidenceId}' at {owner}.");
            }
        }

        return violations;
    }

    private List<string> ValidateGuidanceCitations(MigrationPlanV1 plan)
    {
        var violations = new List<string>();

        foreach (var (owner, guidanceId) in CollectStringArray(plan, "guidanceIds"))
        {
            if (!_guidanceStore.TryGet(guidanceId, out _))
            {
                violations.Add($"Plan cites unknown guidanceId '{guidanceId}' at {owner}.");
            }
        }

        return violations;
    }

    private static HashSet<string> BuildAssessmentEvidenceIds(RepositoryAssessmentV1 assessment)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var dep in assessment.Dependencies)
        {
            if (!string.IsNullOrEmpty(dep.EvidenceId)) ids.Add(dep.EvidenceId);
        }
        foreach (var code in assessment.CodeFindings)
        {
            if (!string.IsNullOrEmpty(code.EvidenceId)) ids.Add(code.EvidenceId);
        }
        if (!string.IsNullOrEmpty(assessment.BuildFindings.EvidenceId))
        {
            ids.Add(assessment.BuildFindings.EvidenceId);
        }
        foreach (var unknown in assessment.Unknowns)
        {
            if (unknown.EvidenceIds is null) continue;
            foreach (var id in unknown.EvidenceIds)
            {
                if (!string.IsNullOrEmpty(id)) ids.Add(id);
            }
        }
        return ids;
    }

    private static IEnumerable<(string Owner, string Value)> CollectStringArray(MigrationPlanV1 plan, string arrayName)
    {
        if (plan.AdditionalProperties is null)
        {
            yield break;
        }
        foreach (var kv in plan.AdditionalProperties)
        {
            foreach (var pair in CollectFromElement(kv.Key, kv.Value, arrayName))
            {
                yield return pair;
            }
        }
    }

    private static IEnumerable<(string Owner, string Value)> CollectFromElement(
        string path, JsonElement element, string arrayName)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.NameEquals(arrayName) && property.Value.ValueKind == JsonValueKind.Array)
                    {
                        var idx = 0;
                        foreach (var item in property.Value.EnumerateArray())
                        {
                            if (item.ValueKind == JsonValueKind.String)
                            {
                                var value = item.GetString();
                                if (!string.IsNullOrEmpty(value))
                                {
                                    yield return ($"{path}.{arrayName}[{idx}]", value);
                                }
                            }
                            idx++;
                        }
                    }
                    else
                    {
                        foreach (var nested in CollectFromElement($"{path}.{property.Name}", property.Value, arrayName))
                        {
                            yield return nested;
                        }
                    }
                }
                break;
            case JsonValueKind.Array:
                var arrayIdx = 0;
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var nested in CollectFromElement($"{path}[{arrayIdx}]", item, arrayName))
                    {
                        yield return nested;
                    }
                    arrayIdx++;
                }
                break;
        }
    }

    private static void CheckElementForUnsafeText(string path, JsonElement element, List<string> violations)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                {
                    var text = element.GetString();
                    if (text is null)
                    {
                        return;
                    }

                    foreach (var phrase in UnsafePhrases)
                    {
                        if (text.Contains(phrase, StringComparison.OrdinalIgnoreCase))
                        {
                            violations.Add($"Unsafe instruction detected at '{path}': contains '{phrase}'.");
                        }
                    }

                    break;
                }
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    CheckElementForUnsafeText($"{path}.{property.Name}", property.Value, violations);
                }

                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    CheckElementForUnsafeText($"{path}[{index}]", item, violations);
                    index++;
                }

                break;
        }
    }
}
