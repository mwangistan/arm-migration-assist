using System.Text;
using System.Text.Json;
using MigrationPlanner.Assessment;
using MigrationPlanner.Plan;
using MigrationPlanner.Scoring;
using Microsoft.Extensions.Logging;

namespace MigrationPlanner.Llm;

/// <summary>
/// Second-pass narrative filler. The plan skeleton is already schema-valid.
/// This step asks gpt-4o to write short natural-language text for a defined
/// set of slot keys and returns a flat <c>{slotId: text}</c> map. The server
/// merges those strings into the skeleton by copy. The LLM cannot invalidate
/// the schema because it never emits structure.
/// </summary>
public sealed class NarrativeFiller
{
    private readonly LlmClient _llm;
    private readonly LlmOptions _options;
    private readonly ILogger<NarrativeFiller> _logger;

    private static readonly JsonSerializerOptions ResponseJson = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public NarrativeFiller(LlmClient llm, LlmOptions options, ILogger<NarrativeFiller> logger)
    {
        _llm = llm;
        _options = options;
        _logger = logger;
    }

    public async Task<MigrationPlanV1> FillAsync(
        MigrationPlanV1 skeleton, RepositoryAssessmentV1 assessment, ReadinessScoreV1 score,
        CancellationToken cancellationToken)
    {
        var slots = BuildSlotDefinitions(skeleton);
        if (slots.Count == 0)
        {
            return skeleton;
        }

        var systemPrompt = BuildSystemPrompt();
        var userPrompt = BuildUserPrompt(skeleton, assessment, score, slots);

        Dictionary<string, string> map;
        try
        {
            var raw = await _llm.CompleteJsonAsync(systemPrompt, userPrompt, cancellationToken).ConfigureAwait(false);
            map = ParseSlotMap(raw);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "narrative_pass_failed; returning skeleton with placeholders.");
            return skeleton;
        }

        return MergeSlotsIntoSkeleton(skeleton, map);
    }

    private static IReadOnlyList<string> BuildSlotDefinitions(MigrationPlanV1 skeleton)
    {
        var slots = new List<string> { "executiveSummary", "scoreInterpretation" };
        foreach (var w in skeleton.WorkItems)
        {
            slots.Add($"workItem.{w.Id}.objective");
        }
        foreach (var r in skeleton.Risks)
        {
            slots.Add($"risk.{r.Id}.mitigation");
        }
        foreach (var m in skeleton.MissingSkills)
        {
            slots.Add($"missingSkill.{m.ProposedName}.justification");
        }
        return slots;
    }

    private static string BuildSystemPrompt() =>
        """
        You are the ARM Migration Assist narrator. You are given a validated
        migration plan skeleton and a slot manifest. For each slot in the
        manifest you write a short, plain-language English text.

        Hard rules:
          1. Output exactly one JSON object. No prose, no code fences.
          2. Top-level keys are the slot ids from the manifest, verbatim.
          3. Each value is a plain-language string. No JSON, no lists,
             no Markdown, no code.
          4. Do NOT invent or reference entities that are not already in
             the skeleton (no new work items, evidenceIds, dependencies,
             or files).
          5. Keep each string within the character limit stated per slot.
        """;

    private string BuildUserPrompt(
        MigrationPlanV1 skeleton, RepositoryAssessmentV1 assessment, ReadinessScoreV1 score,
        IReadOnlyList<string> slots)
    {
        var sb = new StringBuilder();

        sb.AppendLine("Fill the following narrative slots.");
        sb.AppendLine();
        sb.AppendLine("=== PLAN SUMMARY (context) ===");
        sb.Append("assessmentId : ").AppendLine(skeleton.AssessmentId);
        sb.Append("recommendedPath : ").AppendLine(skeleton.RecommendedPath);
        sb.Append("confidence : ").AppendLine(skeleton.Confidence);
        sb.Append("band : ").AppendLine(score.Band.ToString());
        sb.Append("overallScore : ").AppendLine(score.OverallScore.ToString());
        sb.Append("scoreSummary : ").AppendLine(score.ScoreSummary);
        sb.Append("majorBlockers : ").AppendLine(string.Join("; ", score.MajorBlockers.Select(b => b.Description)));

        sb.AppendLine();
        sb.AppendLine("=== WORK ITEMS ===");
        foreach (var w in skeleton.WorkItems)
        {
            sb.Append("- ").Append(w.Id).Append(" [").Append(w.Priority).Append("] ")
              .Append(w.Title).Append(" (skill=").Append(w.AgentOrSkill)
              .Append(", effort=").Append(w.EstimatedEffort).Append(", risk=").Append(w.Risk).AppendLine(")");
        }

        sb.AppendLine();
        sb.AppendLine("=== RISKS ===");
        foreach (var r in skeleton.Risks)
        {
            sb.Append("- ").Append(r.Id).Append(" [").Append(r.Severity).Append("] ").AppendLine(r.Description);
        }

        sb.AppendLine();
        sb.AppendLine("=== MISSING SKILLS ===");
        foreach (var m in skeleton.MissingSkills)
        {
            sb.Append("- ").Append(m.ProposedName).Append(" : ").AppendLine(m.Purpose);
        }

        sb.AppendLine();
        sb.AppendLine("=== SLOT MANIFEST ===");
        sb.AppendLine("Fill EVERY key below with a plain-language string that fits the character limit.");
        var summaryLimit = Math.Min(_options.MaxNarrativeChars, 1800);
        var perItemLimit = 400;
        foreach (var slot in slots)
        {
            var limit = slot is "executiveSummary" or "scoreInterpretation" ? summaryLimit : perItemLimit;
            sb.Append("  ").Append(slot).Append(" : \u2264 ").Append(limit).AppendLine(" chars");
        }

        sb.AppendLine();
        sb.AppendLine("Guidance for tone:");
        sb.AppendLine("  executiveSummary : 1-2 short paragraphs. State the recommended path, the reason, and the first two moves.");
        sb.AppendLine("  scoreInterpretation : explain which dimensions and caps drove the score.");
        sb.AppendLine("  workItem.<id>.objective : one sentence describing what this work item accomplishes.");
        sb.AppendLine("  risk.<id>.mitigation : one sentence describing the concrete mitigation.");
        sb.AppendLine("  missingSkill.<name>.justification : one sentence describing why this skill is needed.");
        sb.AppendLine();
        sb.AppendLine("Return the JSON object now.");

        return sb.ToString();
    }

    private static Dictionary<string, string> ParseSlotMap(string raw)
    {
        var jsonStart = raw.IndexOf('{');
        var jsonEnd = raw.LastIndexOf('}');
        var body = jsonStart >= 0 && jsonEnd > jsonStart ? raw[jsonStart..(jsonEnd + 1)] : raw;

        using var doc = JsonDocument.Parse(body);
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.String)
            {
                map[prop.Name] = prop.Value.GetString() ?? string.Empty;
            }
        }
        return map;
    }

    private MigrationPlanV1 MergeSlotsIntoSkeleton(MigrationPlanV1 skeleton, IReadOnlyDictionary<string, string> map)
    {
        string TakeOrKeep(string slot, string current, int cap)
        {
            if (!map.TryGetValue(slot, out var text) || string.IsNullOrWhiteSpace(text))
            {
                return current;
            }
            return text.Length <= cap ? text : text[..cap];
        }

        var summaryCap = Math.Min(_options.MaxNarrativeChars, 2000);

        var newWorkItems = skeleton.WorkItems
            .Select(w => w with { Objective = TakeOrKeep($"workItem.{w.Id}.objective", w.Objective, 1500) })
            .ToArray();

        var newRisks = skeleton.Risks
            .Select(r => r with { Mitigation = TakeOrKeep($"risk.{r.Id}.mitigation", r.Mitigation, 1000) })
            .ToArray();

        var newMissingSkills = skeleton.MissingSkills
            .Select(m => m with { Justification = TakeOrKeep($"missingSkill.{m.ProposedName}.justification", m.Justification, 1000) })
            .ToArray();

        return skeleton with
        {
            ExecutiveSummary = TakeOrKeep("executiveSummary", skeleton.ExecutiveSummary, summaryCap),
            ScoreInterpretation = TakeOrKeep("scoreInterpretation", skeleton.ScoreInterpretation, summaryCap),
            WorkItems = newWorkItems,
            Risks = newRisks,
            MissingSkills = newMissingSkills,
        };
    }
}
