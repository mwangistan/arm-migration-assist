using System.Globalization;
using MigrationPlanner.Assessment;
using MigrationPlanner.Scoring;

namespace MigrationPlanner.Plan;
/// <summary>
/// Deterministic assembler that produces a schema-complete
/// <see cref="MigrationPlanV1"/> from a scored assessment. Every required
/// field is populated with typed content derived from the inputs. Narrative
/// prose fields (executiveSummary, scoreInterpretation, workItem.objective,
/// risk.description, risk.mitigation, alternative.rationale) get short
/// deterministic placeholders — the LLM narrative pass overwrites them.
/// </summary>
public static class PlanSkeletonBuilder
{
    private const string PlaceholderNarrative = "Narrative pending.";
    private const string DefaultCorpusVersion = "2026-09-15.1";

    public static MigrationPlanV1 Build(
        RepositoryAssessmentV1 assessment,
        ReadinessScoreV1 score,
        ModelProvenance provenance,
        string planId,
        DateTimeOffset generatedAt,
        string corpusVersion)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        ArgumentNullException.ThrowIfNull(score);
        ArgumentNullException.ThrowIfNull(provenance);
        ArgumentException.ThrowIfNullOrWhiteSpace(planId);

        var (recommendedPath, confidence) = RecommendationDispatch.Choose(score);
        var scoreDigest = ScoreDigest.Compute(score);
        var isInsufficient = recommendedPath == RecommendationDispatch.PathInsufficientEvidence;

        var buckets = isInsufficient
            ? Array.Empty<GranularityCalculator.ExpectedBucket>()
            : GranularityCalculator.Compute(assessment, score).Buckets.ToArray();

        var workItems = BuildWorkItems(buckets, isInsufficient);
        var missingSkills = BuildMissingSkills(assessment, workItems);
        var alternatives = BuildAlternatives(score, recommendedPath);
        var risks = BuildRisks(assessment, score, isInsufficient);
        var validationPlan = BuildValidationPlan(assessment, isInsufficient);
        var unknowns = BuildUnknowns(assessment);
        var approvals = BuildApprovals(workItems);
        var reusable = BuildReusableOutputs(isInsufficient);

        return new MigrationPlanV1
        {
            SchemaVersion = "1.0",
            PlanId = planId,
            AssessmentId = assessment.AssessmentId,
            GeneratedAt = generatedAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture),
            ModelProvenance = provenance,
            ScoreDigest = scoreDigest,
            CorpusVersion = string.IsNullOrWhiteSpace(corpusVersion) ? DefaultCorpusVersion : corpusVersion,
            RecommendedPath = recommendedPath,
            Confidence = confidence,
            ExecutiveSummary = PlaceholderNarrative,
            ScoreInterpretation = string.IsNullOrWhiteSpace(score.ScoreSummary) ? PlaceholderNarrative : score.ScoreSummary,
            Facts = Array.Empty<Statement>(),
            Inferences = Array.Empty<Inference>(),
            Alternatives = alternatives,
            WorkItems = workItems,
            MissingSkills = missingSkills,
            ValidationPlan = validationPlan,
            Risks = risks,
            Unknowns = unknowns,
            RequiredApprovals = approvals,
            ReusableOutputs = reusable,
        };
    }

    private static IReadOnlyList<WorkItem> BuildWorkItems(
        IReadOnlyList<GranularityCalculator.ExpectedBucket> buckets, bool isInsufficient)
    {
        if (isInsufficient || buckets.Count == 0)
        {
            return Array.Empty<WorkItem>();
        }

        var items = new List<WorkItem>(buckets.Count);
        var seq = 1;
        foreach (var b in buckets)
        {
            var slug = Slugify(b.Description);
            var category = b.Category;
            var id = $"wi-{category}-{slug}";
            var skill = MapSkill(category);
            var (effort, risk) = MapEffortRisk(category);

            items.Add(new WorkItem
            {
                Id = id,
                Sequence = seq++,
                Priority = category == "dep" || category == "code" ? "P0" : "P1",
                Title = Truncate(b.Description, 200),
                Objective = PlaceholderNarrative,
                AgentOrSkill = skill,
                Inputs = Array.Empty<string>(),
                ExpectedOutputs = Array.Empty<string>(),
                Dependencies = Array.Empty<string>(),
                EvidenceIds = b.EvidenceIds.ToArray(),
                GuidanceIds = Array.Empty<string>(),
                AcceptanceTests = new[]
                {
                    new AcceptanceTest
                    {
                        Id = $"at-{category}-{slug}-build",
                        Description = "Repository builds cleanly for the target architecture.",
                        ExpectedOutcome = "Build succeeds with no errors specific to this work item.",
                    },
                    new AcceptanceTest
                    {
                        Id = $"at-{category}-{slug}-func",
                        Description = "Functional smoke check passes on the target architecture.",
                        ExpectedOutcome = "Existing functional test suite passes.",
                    },
                },
                ApprovalRequired = true,
                EstimatedEffort = effort,
                Risk = risk,
            });
        }
        return items;
    }

    private static IReadOnlyList<MissingSkill> BuildMissingSkills(
        RepositoryAssessmentV1 assessment, IReadOnlyList<WorkItem> workItems)
    {
        if (workItems.Count == 0) return Array.Empty<MissingSkill>();

        var available = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in assessment.AvailableSkills)
        {
            if (!string.IsNullOrWhiteSpace(s.Name)) available.Add(s.Name);
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var declared = new List<MissingSkill>();
        foreach (var w in workItems)
        {
            if (available.Contains(w.AgentOrSkill) || !seen.Add(w.AgentOrSkill)) continue;

            declared.Add(new MissingSkill
            {
                ProposedName = w.AgentOrSkill,
                Purpose = $"Skill required to complete '{w.Title}'.",
                RequiredInputs = new[] { "repository-context" },
                ExpectedOutputs = new[] { "code-or-build-changes" },
                Justification = PlaceholderNarrative,
                EvidenceIds = w.EvidenceIds,
                WriteAccess = false,
            });
        }
        return declared;
    }

    private static IReadOnlyList<Alternative> BuildAlternatives(ReadinessScoreV1 score, string recommendedPath)
    {
        var considered = new[]
        {
            RecommendationDispatch.PathNativeArm64,
            RecommendationDispatch.PathArm64Ec,
            RecommendationDispatch.PathStaged,
            RecommendationDispatch.PathWinUi3,
        };

        var evidenceIds = score.EvidenceIds.Take(5).ToArray();
        var list = new List<Alternative>(considered.Length);

        foreach (var path in considered)
        {
            var isRecommended = path == recommendedPath;
            list.Add(new Alternative
            {
                Path = path,
                Disposition = isRecommended ? "viable" : "deferred",
                Rationale = isRecommended
                    ? $"Selected by scoring ruleset '{score.Producer.Ruleset}' (band='{ToWire(score.Band)}', overall={score.OverallScore}/100)."
                    : $"Not selected under this scoring pass; may be revisited if evidence changes.",
                EvidenceIds = evidenceIds,
                GuidanceIds = Array.Empty<string>(),
                EstimatedEffort = null,
                Risk = null,
            });
        }

        if (recommendedPath == RecommendationDispatch.PathInsufficientEvidence)
        {
            list.Add(new Alternative
            {
                Path = RecommendationDispatch.PathInsufficientEvidence,
                Disposition = "viable",
                Rationale = "Insufficient evidence to recommend a specific migration path; gather more assessment data first.",
                EvidenceIds = evidenceIds,
                GuidanceIds = Array.Empty<string>(),
            });
        }
        return list;
    }

    private static IReadOnlyList<Risk> BuildRisks(
        RepositoryAssessmentV1 assessment, ReadinessScoreV1 score, bool isInsufficient)
    {
        var risks = new List<Risk>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var b in score.MajorBlockers)
        {
            var id = $"rk-{Slugify(b.BlockerId)}";
            if (!seenIds.Add(id)) continue;
            risks.Add(new Risk
            {
                Id = id,
                Description = b.Description,
                Severity = "high",
                Mitigation = PlaceholderNarrative,
                EvidenceIds = b.EvidenceIds,
            });
        }

        foreach (var dep in assessment.Dependencies)
        {
            if (dep.Criticality != Criticality.Required) continue;
            var status = dep.ArchitectureStatus;
            if (status != ArchitectureStatus.Blocked && status != ArchitectureStatus.EmulationOnly) continue;
            var id = $"rk-dep-{Slugify(dep.Name)}";
            if (!seenIds.Add(id)) continue;
            risks.Add(new Risk
            {
                Id = id,
                Description = $"Required dependency '{dep.Name}' has architecture status '{ArchitectureStatusWire(status)}'.",
                Severity = status == ArchitectureStatus.Blocked ? "critical" : "high",
                Mitigation = PlaceholderNarrative,
                EvidenceIds = new[] { dep.EvidenceId },
            });
        }

        if (risks.Count == 0 && !isInsufficient)
        {
            risks.Add(new Risk
            {
                Id = "rk-baseline-arm64-parity",
                Description = "General risk of behavioral or performance regressions on Windows on Arm relative to x64.",
                Severity = "medium",
                Mitigation = PlaceholderNarrative,
                EvidenceIds = score.EvidenceIds.Take(3).ToArray(),
            });
        }
        return risks;
    }

    private static ValidationPlan BuildValidationPlan(RepositoryAssessmentV1 assessment, bool isInsufficient)
    {
        return new ValidationPlan
        {
            TargetDevices = new[] { "arm64-vm", "snapdragon-x-series" },
            BuildChecks = isInsufficient ? Array.Empty<ValidationCheck>() : new[]
            {
                new ValidationCheck
                {
                    Id = "vc-build-arm64",
                    Description = "Repository builds an ARM64 or Arm64EC binary.",
                    ExpectedOutcome = "Build pipeline produces the appropriate ARM binary.",
                },
            },
            FunctionalChecks = isInsufficient ? Array.Empty<ValidationCheck>() : new[]
            {
                new ValidationCheck
                {
                    Id = "vc-func-smoke",
                    Description = "Application launches and completes a smoke workflow on ARM hardware.",
                    ExpectedOutcome = "Smoke workflow completes with no errors.",
                },
            },
            ReliabilityChecks = Array.Empty<ValidationCheck>(),
            PerformanceChecks = Array.Empty<ValidationCheck>(),
            PowerChecks = Array.Empty<ValidationCheck>(),
            OfflineChecks = Array.Empty<ValidationCheck>(),
            AccessibilityChecks = Array.Empty<ValidationCheck>(),
            WindowsExperienceChecks = Array.Empty<ValidationCheck>(),
        };
    }

    private static IReadOnlyList<PlanUnknown> BuildUnknowns(RepositoryAssessmentV1 assessment)
    {
        if (assessment.Unknowns is null || assessment.Unknowns.Count == 0)
        {
            return Array.Empty<PlanUnknown>();
        }
        var list = new List<PlanUnknown>(assessment.Unknowns.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var counter = 0;
        foreach (var u in assessment.Unknowns)
        {
            counter++;
            var id = $"uk-{Slugify(u.Description)}";
            if (id.Length <= 3) id = $"uk-item-{counter}";
            if (!seen.Add(id)) id = $"uk-item-{counter}";
            list.Add(new PlanUnknown
            {
                Id = id,
                Description = string.IsNullOrWhiteSpace(u.Description) ? "Unresolved question from the assessment." : u.Description,
                RequiredSkill = null,
                EvidenceIds = u.EvidenceIds?.ToArray() ?? Array.Empty<string>(),
            });
        }
        return list;
    }

    private static IReadOnlyList<Approval> BuildApprovals(IReadOnlyList<WorkItem> workItems)
    {
        if (workItems.Count == 0) return Array.Empty<Approval>();
        return new[]
        {
            new Approval
            {
                ApprovalId = "ap-plan-execution",
                Summary = "Human approval required before executing any workItem.",
                WorkItemIds = workItems.Select(w => w.Id).ToArray(),
            },
        };
    }

    private static IReadOnlyList<ReusableOutput> BuildReusableOutputs(bool isInsufficient)
    {
        if (isInsufficient) return Array.Empty<ReusableOutput>();
        return new[]
        {
            new ReusableOutput
            {
                Name = "arm-migration-plan-template",
                Kind = "template",
                Description = "Structured Windows-on-Arm migration plan produced by the deterministic skeleton + narrative filler.",
            },
        };
    }

    private static string MapSkill(string category) => category switch
    {
        "dep"   => "dependency/source-build",
        "build" => "build/add-ci-job",
        "code"  => "code/refactor",
        _        => "general/planning",
    };

    private static (string Effort, string Risk) MapEffortRisk(string category) => category switch
    {
        "dep"   => ("large", "high"),
        "build" => ("medium", "medium"),
        "code"  => ("medium", "medium"),
        _        => ("small", "low"),
    };

    private static string ToWire(ReadinessBand band) => band switch
    {
        ReadinessBand.ReadyOrMinorChanges => "ready-or-minor-changes",
        ReadinessBand.ModerateMigration => "moderate-migration",
        ReadinessBand.SignificantRemediation => "significant-remediation",
        ReadinessBand.BlockedOrMajorRedesign => "blocked-or-major-redesign",
        ReadinessBand.InsufficientEvidence => "insufficient-evidence",
        _ => "insufficient-evidence",
    };

    private static string ArchitectureStatusWire(ArchitectureStatus status) => status switch
    {
        ArchitectureStatus.Ready => "ready",
        ArchitectureStatus.EmulationOnly => "emulation-only",
        ArchitectureStatus.Blocked => "blocked",
        ArchitectureStatus.Unknown => "unknown",
        _ => "unknown",
    };

    private static string Slugify(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "unknown";
        var sb = new System.Text.StringBuilder(text.Length);
        var lastDash = false;
        foreach (var ch in text.ToLowerInvariant())
        {
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                sb.Append(ch);
                lastDash = false;
            }
            else if (!lastDash && sb.Length > 0)
            {
                sb.Append('-');
                lastDash = true;
            }
        }
        var slug = sb.ToString().Trim('-');
        if (slug.Length < 2) slug = "x" + slug;
        if (slug.Length > 40) slug = slug[..40].TrimEnd('-');
        return slug;
    }

    private static string Truncate(string text, int max)
        => string.IsNullOrEmpty(text) ? "" : (text.Length <= max ? text : text[..max]);
}
