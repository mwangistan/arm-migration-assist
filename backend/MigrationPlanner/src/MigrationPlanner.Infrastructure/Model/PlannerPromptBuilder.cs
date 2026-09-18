using System.Text;
using System.Text.Json;
using MigrationPlanner.Application.Abstractions;
using MigrationPlanner.Application.Planning;
using MigrationPlanner.Domain.Assessment;
using MigrationPlanner.Domain.Plan;

namespace MigrationPlanner.Infrastructure.Model;

/// <summary>
/// Assembles the system + user prompts for the hosted Phi model. Emits the
/// guidance corpus <em>index only</em> — snippet excerpts are not inlined so
/// context stays small and the model is forced to cite <c>guidanceId</c>
/// values the validator can resolve.
/// </summary>
internal static class PlannerPromptBuilder
{
    private static readonly JsonSerializerOptions PromptJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public static string BuildSystemPrompt() => SystemPrompt;

    public static string BuildUserPrompt(
        RepositoryAssessmentV1 assessment,
        ReadinessScoreV1 score,
        IGuidanceLookup guidanceLookup,
        PlannerProvenance provenance,
        PlannerRetryHint? retryHint = null,
        bool enableGuidanceLookupTool = false)
    {
        var index = guidanceLookup.ListIndex();
        var scoreDigest = ScoreDigest.Compute(score);

        var payload = new
        {
            assessment,
            deterministicScore = score,
            scoreDigest,
            guidanceIndex = index,
            corpusVersion = guidanceLookup.CorpusVersion,
        };

        var json = JsonSerializer.Serialize(payload, PromptJson);

        var builder = new StringBuilder(json.Length + 1024);
        builder.AppendLine("Produce the MigrationPlanV1 JSON for the following planning input.");
        builder.AppendLine("Copy assessmentId, scoreDigest, and corpusVersion byte-for-byte.");
        builder.AppendLine("Input document follows:");
        builder.AppendLine();
        builder.Append(json);

        AppendModelProvenance(builder, provenance);
        AppendGranularityExpectations(builder, assessment, score);
        AppendAllowedEvidenceIds(builder, assessment);
        AppendSkillIoAllowlist(builder, assessment);

        if (enableGuidanceLookupTool)
        {
            AppendGuidanceLookupToolUsage(builder);
        }

        if (retryHint is not null)
        {
            builder.AppendLine();
            builder.AppendLine();
            builder.AppendLine("=== RETRY CORRECTION ===");
            builder.AppendLine("Your previous attempt was rejected by the server. Start from the JSON");
            builder.AppendLine("under 'Previous plan' below and reproduce it VERBATIM, changing ONLY");
            builder.AppendLine("the specific fields the correction rules call out.");
            builder.AppendLine();
            builder.AppendLine($"Server diagnostic        : {retryHint.Diagnostic}");
            builder.AppendLine();

            if (retryHint.Reason == PlannerRetryReason.RecommendationInconsistent)
            {
                builder.AppendLine($"Previous recommendedPath : \"{retryHint.PreviousRecommendedPath}\"");
                builder.AppendLine($"Previous confidence      : \"{retryHint.PreviousConfidence}\"");
                builder.AppendLine($"Required recommendedPath : \"{retryHint.ExpectedRecommendedPath}\"");
                builder.AppendLine($"Required confidence      : \"{retryHint.ExpectedConfidence}\"");
                builder.AppendLine();
                builder.AppendLine("Rules for the retry (STRICT):");
                builder.AppendLine("  1. Copy the entire 'Previous plan' JSON below into your output.");
                builder.AppendLine("  2. Change recommendedPath to the Required value.");
                builder.AppendLine("  3. Change confidence to the Required value.");
                builder.AppendLine("  4. In alternatives[], flip the disposition of the entry whose");
                builder.AppendLine("     path == Required recommendedPath to \"viable\", and flip the");
                builder.AppendLine("     entry whose path == Previous recommendedPath to \"rejected\"");
                builder.AppendLine("     or \"deferred\".");
                builder.AppendLine("  5. Do NOT rewrite, reword, or reorder any other field. Every");
                builder.AppendLine("     other value must match the Previous plan character-for-character.");
                builder.AppendLine("  6. Output the corrected JSON only. No prose.");
            }
            else if (retryHint.Reason == PlannerRetryReason.SkillMissing)
            {
                var unresolved = retryHint.UnresolvedSkills ?? Array.Empty<string>();
                builder.AppendLine("Unresolved skills (currently cited by workItems but neither in");
                builder.AppendLine("assessment.availableSkills nor declared in plan.missingSkills):");
                foreach (var skill in unresolved)
                {
                    builder.AppendLine($"  - \"{skill}\"");
                }
                builder.AppendLine();
                builder.AppendLine("Rules for the retry (STRICT):");
                builder.AppendLine("  1. Copy the entire 'Previous plan' JSON below into your output.");
                builder.AppendLine("  2. For each unresolved skill listed above, add ONE new entry to");
                builder.AppendLine("     missingSkills[] whose proposedName equals that skill name.");
                builder.AppendLine("     Fill in purpose, requiredInputs, expectedOutputs,");
                builder.AppendLine("     justification, and evidenceIds. writeAccess=false is fine.");
                builder.AppendLine("  3. Keep every workItems[].agentOrSkill reference unchanged; they");
                builder.AppendLine("     are now valid because the same names are declared in");
                builder.AppendLine("     missingSkills[].");
                builder.AppendLine("  4. Do NOT rewrite, reword, or reorder any other field. Every");
                builder.AppendLine("     value outside missingSkills[] must match the Previous plan");
                builder.AppendLine("     character-for-character. Do not change alternatives, risks,");
                builder.AppendLine("     unknowns, validationPlan, requiredApprovals, or anything else.");
                builder.AppendLine("  5. Output the corrected JSON only. No prose.");
            }
            else if (retryHint.Reason == PlannerRetryReason.ShapeInvalid)
            {
                builder.AppendLine("Rules for the retry (STRICT):");
                builder.AppendLine("  1. Copy the entire 'Previous plan' JSON below into your output.");
                builder.AppendLine("  2. Fix ONLY the specific fields the Server diagnostic names.");
                builder.AppendLine("     Common causes:");
                builder.AppendLine("       - alternatives[].estimatedEffort must be one of");
                builder.AppendLine("         { \"small\", \"medium\", \"large\", \"unknown\" }.");
                builder.AppendLine("       - alternatives[].risk must be one of");
                builder.AppendLine("         { \"low\", \"medium\", \"high\", \"critical\" }.");
                builder.AppendLine("       - workItems[].estimatedEffort and workItems[].risk use the");
                builder.AppendLine("         same closed sets.");
                builder.AppendLine("       - unknowns[].requiredSkill must be null OR a SkillReference");
                builder.AppendLine("         (kebab-case with optional '/' namespaces, all lowercase).");
                builder.AppendLine("       - risks[].severity must be one of");
                builder.AppendLine("         { \"low\", \"medium\", \"high\", \"critical\" }.");
                builder.AppendLine("       - workItems[].priority must be one of");
                builder.AppendLine("         { \"P0\", \"P1\", \"P2\" }.");
                builder.AppendLine("       - alternatives[].disposition must be one of");
                builder.AppendLine("         { \"rejected\", \"deferred\", \"viable\" }.");
                builder.AppendLine("  3. Do NOT rewrite, reword, or reorder any other field. Every");
                builder.AppendLine("     other value must match the Previous plan character-for-character.");
                builder.AppendLine("  4. Output the corrected JSON only. No prose.");
            }
            else if (retryHint.Reason == PlannerRetryReason.MissingEvidence)
            {
                var invalid = retryHint.InvalidEvidenceIds ?? Array.Empty<string>();
                var allowed = retryHint.AllowedEvidenceIds ?? Array.Empty<string>();

                builder.AppendLine("Invented evidenceId(s) currently cited by the previous plan");
                builder.AppendLine("(they do NOT exist in the assessment and MUST be removed):");
                foreach (var id in invalid)
                {
                    builder.AppendLine($"  - \"{id}\"");
                }
                builder.AppendLine();
                builder.AppendLine("The COMPLETE list of evidenceIds that DO exist in this assessment.");
                builder.AppendLine("You may cite only these values. Any other value will be rejected:");
                foreach (var id in allowed)
                {
                    builder.AppendLine($"  - \"{id}\"");
                }
                builder.AppendLine();
                builder.AppendLine("Rules for the retry (STRICT):");
                builder.AppendLine("  1. Copy the entire 'Previous plan' JSON below into your output.");
                builder.AppendLine("  2. Find every occurrence of each invented evidenceId above and");
                builder.AppendLine("     replace it with the correct value from the allowed list. If");
                builder.AppendLine("     you cannot determine which allowed id was intended, remove");
                builder.AppendLine("     the invented id from that evidenceIds array. Empty arrays");
                builder.AppendLine("     are permitted.");
                builder.AppendLine("  3. Copy allowed evidenceId values CHARACTER-FOR-CHARACTER. They");
                builder.AppendLine("     contain long hex suffixes; count the digits and echo each");
                builder.AppendLine("     one exactly.");
                builder.AppendLine("  4. Do NOT rewrite, reword, or reorder any other field. Every");
                builder.AppendLine("     other value must match the Previous plan character-for-character.");
                builder.AppendLine("  5. Output the corrected JSON only. No prose.");
            }
            else if (retryHint.Reason == PlannerRetryReason.SkillIoMismatch)
            {
                var violations = retryHint.SkillIoViolations ?? Array.Empty<SkillIoViolation>();
                var allowlists = retryHint.SkillIoAllowlists
                    ?? new Dictionary<string, SkillIoAllowlist>(StringComparer.Ordinal);

                builder.AppendLine("Invalid workItems[] input/output values currently cited by the");
                builder.AppendLine("previous plan (these are NOT in the referenced skill's declared");
                builder.AppendLine("supportedInputs/supportedOutputs and MUST be replaced or removed):");
                foreach (var v in violations)
                {
                    builder.AppendLine(
                        $"  - workItems[{v.WorkItemIndex}].{v.FieldName} cites \"{v.InvalidValue}\" for skill \"{v.SkillName}\"");
                }
                builder.AppendLine();
                builder.AppendLine("Allowed values per referenced skill. Pick workItems[].inputs from");
                builder.AppendLine("that skill's supportedInputs list; pick workItems[].expectedOutputs");
                builder.AppendLine("from its supportedOutputs list. Empty arrays are permitted:");
                foreach (var (name, io) in allowlists)
                {
                    builder.Append("  ").AppendLine(name);
                    builder.Append("    supportedInputs  : ");
                    builder.AppendLine(io.Inputs.Count == 0 ? "(none)" : string.Join(", ", io.Inputs));
                    builder.Append("    supportedOutputs : ");
                    builder.AppendLine(io.Outputs.Count == 0 ? "(none)" : string.Join(", ", io.Outputs));
                }
                builder.AppendLine();
                builder.AppendLine("Rules for the retry (STRICT):");
                builder.AppendLine("  1. Copy the entire 'Previous plan' JSON below into your output.");
                builder.AppendLine("  2. For each invalid value listed above, either replace it with");
                builder.AppendLine("     a value drawn from the same skill's allowed list, or remove");
                builder.AppendLine("     it. Empty inputs/expectedOutputs arrays are permitted.");
                builder.AppendLine("  3. If the invalid value looks like an evidence identifier (long");
                builder.AppendLine("     hex suffix), it belongs in workItems[].evidenceIds, NOT in");
                builder.AppendLine("     workItems[].inputs. Move it there if it is present in the");
                builder.AppendLine("     assessment; otherwise drop it.");
                builder.AppendLine("  4. Do NOT rewrite, reword, or reorder any other field. Every");
                builder.AppendLine("     other value must match the Previous plan character-for-character.");
                builder.AppendLine("  5. Output the corrected JSON only. No prose.");
            }
            else if (retryHint.Reason == PlannerRetryReason.UnderGranular)
            {
                var missing = retryHint.MissingBuckets ?? Array.Empty<string>();
                var isSkillList = missing.Count > 0
                    && missing.All(m => m.Contains('/') && !m.Contains(' '));

                if (isSkillList)
                {
                    builder.AppendLine("The plan omitted required skills. Add one NEW workItems[]");
                    builder.AppendLine("entry per skill listed below:");
                    foreach (var skill in missing)
                    {
                        builder.AppendLine($"  - {skill}");
                    }
                }
                else
                {
                    builder.AppendLine("The plan did not satisfy the granularity contract. The server");
                    builder.AppendLine("reports the following bucket coverage violations:");
                    foreach (var m in missing)
                    {
                        builder.AppendLine($"  - {m}");
                    }
                }
                builder.AppendLine();
                builder.AppendLine("Rules for the retry (STRICT):");
                builder.AppendLine("  1. Copy the entire 'Previous plan' JSON below into your output.");
                builder.AppendLine("  2. For EACH required skill above, add a NEW workItems[] entry.");
                builder.AppendLine("     Do NOT re-assign the skill on an existing workItem — add new ones.");
                builder.AppendLine("     Do NOT substitute `build/add-arm64-target` (rule 16.5).");
                builder.AppendLine("  3. If the previous plan added `build/add-arm64-target` to");
                builder.AppendLine("     `missingSkills[]` as a workaround, REMOVE that entry — it");
                builder.AppendLine("     is a runnable catalog skill, not a missing capability.");
                builder.AppendLine("  4. Each new workItem uses this template (fill title/objective");
                builder.AppendLine("     with concrete text; keep the other fields exactly as shown):");
                builder.AppendLine();
                builder.AppendLine("     {");
                builder.AppendLine("       \"id\": \"wi-<short-kebab-from-skill-suffix>\",");
                builder.AppendLine("       \"sequence\": <next-integer>,");
                builder.AppendLine("       \"priority\": \"P0\",");
                builder.AppendLine("       \"title\": \"<one-line summary that names the skill's purpose>\",");
                builder.AppendLine("       \"objective\": \"<1-4 sentences: what the audit inspects, where its output goes>\",");
                builder.AppendLine("       \"agentOrSkill\": \"<exact skill name from the list above>\",");
                builder.AppendLine("       \"inputs\": [],");
                builder.AppendLine("       \"expectedOutputs\": [\"report\"],");
                builder.AppendLine("       \"dependencies\": [],");
                builder.AppendLine("       \"evidenceIds\": [<pip-dep evidenceIds from the assessment>],");
                builder.AppendLine("       \"guidanceIds\": [],");
                builder.AppendLine("       \"acceptanceTests\": [");
                builder.AppendLine("         { \"id\": \"at-<short>-generates\", \"description\": \"Runner emits the report file.\",");
                builder.AppendLine("           \"expectedOutcome\": \"A markdown report is produced under .arm-migration/reports/.\" },");
                builder.AppendLine("         { \"id\": \"at-<short>-cites-guidance\", \"description\": \"Report cites the grounding guidance id.\",");
                builder.AppendLine("           \"expectedOutcome\": \"Report references the associated python-woa-* or pytorch-woa-* guidance snippet.\" }");
                builder.AppendLine("       ],");
                builder.AppendLine("       \"approvalRequired\": true,");
                builder.AppendLine("       \"estimatedEffort\": \"small\",");
                builder.AppendLine("       \"risk\": \"low\"");
                builder.AppendLine("     }");
                builder.AppendLine();
                builder.AppendLine("     For `python/pip-constraints-arm64-scaffold`, use");
                builder.AppendLine("     `expectedOutputs`: [\"patch\", \"report\"].");
                builder.AppendLine("  5. Do NOT rewrite, reword, or reorder any other field. Every");
                builder.AppendLine("     other value must match the Previous plan character-for-character.");
                builder.AppendLine("  6. Output the corrected JSON only. No prose.");
            }

            if (!string.IsNullOrWhiteSpace(retryHint.PreviousPlanJson))
            {
                builder.AppendLine();
                builder.AppendLine("=== PREVIOUS PLAN ===");
                builder.AppendLine(retryHint.PreviousPlanJson);
            }
        }

        return builder.ToString();
    }

    private static void AppendModelProvenance(StringBuilder builder, PlannerProvenance provenance)
    {
        builder.AppendLine();
        builder.AppendLine();
        builder.AppendLine("=== REQUIRED modelProvenance VALUES ===");
        builder.AppendLine("Copy these three values into plan.modelProvenance verbatim. Do NOT");
        builder.AppendLine("substitute other model names, versions, or providers.");
        builder.AppendLine($"  provider : \"{provenance.Provider}\"");
        builder.AppendLine($"  name     : \"{provenance.Name}\"");
        builder.AppendLine($"  version  : \"{provenance.Version}\"");
    }

    private static void AppendGuidanceLookupToolUsage(StringBuilder builder)
    {
        builder.AppendLine();
        builder.AppendLine();
        builder.AppendLine("=== TOOL AVAILABLE: lookup_windows_arm_guidance ===");
        builder.AppendLine("The guidanceIndex in this prompt lists only ids + short summaries.");
        builder.AppendLine("Snippet bodies are NOT inlined. You MAY call the function tool");
        builder.AppendLine("lookup_windows_arm_guidance to fetch the full body of a specific");
        builder.AppendLine("snippet before you cite it, using EITHER of these argument shapes:");
        builder.AppendLine("  { \"guidanceId\": \"<id from guidanceIndex>\" }");
        builder.AppendLine("  { \"topic\": \"arm64ec\" | \"arm64-target\" | \"packaging\" | ...}");
        builder.AppendLine();
        builder.AppendLine("Rules:");
        builder.AppendLine("  - Prefer calling this tool AT LEAST ONCE for the highest-priority");
        builder.AppendLine("    guidance you plan to cite (e.g. the snippet you will lean on for");
        builder.AppendLine("    executiveSummary or the top risk). Reading the body catches");
        builder.AppendLine("    nuance the one-line summary misses.");
        builder.AppendLine("  - Do NOT invent guidanceIds. Only ids in the guidanceIndex are");
        builder.AppendLine("    valid tool inputs.");
        builder.AppendLine("  - After you have the information you need, produce the final");
        builder.AppendLine("    MigrationPlanV1 JSON object. Tool calls do not count as the plan.");
        builder.AppendLine("  - You have a hard cap of 4 tool-call rounds. Prefer at most one or");
        builder.AppendLine("    two well-chosen calls.");
    }

    private static void AppendGranularityExpectations(
        StringBuilder builder, RepositoryAssessmentV1 assessment, ReadinessScoreV1 score)
    {
        var expectation = GranularityCalculator.Compute(assessment, score);
        if (expectation.Buckets.Count == 0)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine();
        builder.AppendLine("=== PLAN GRANULARITY EXPECTATIONS (per rule 20) ===");
        builder.AppendLine("This assessment requires at LEAST " + expectation.MinimumWorkItems
            + " workItems, drawn from the following buckets. Producing fewer, or");
        builder.AppendLine("collapsing multiple buckets into a single \"setup\" work item, will fail");
        builder.AppendLine("server validation with HTTP 422 planner.plan.underGranular.");
        builder.AppendLine();
        for (var i = 0; i < expectation.Buckets.Count; i++)
        {
            var b = expectation.Buckets[i];
            builder.Append("  ").Append(i + 1).Append(". [").Append(b.Category).Append("] ")
                .Append(b.Description);
            if (b.EvidenceIds.Count > 0)
            {
                builder.Append("  (evidence: ").Append(string.Join(", ", b.EvidenceIds)).Append(')');
            }
            builder.AppendLine();
            if (!string.IsNullOrEmpty(b.RequiredSkill))
            {
                builder.Append("       -> workItems[].agentOrSkill MUST be \"")
                    .Append(b.RequiredSkill)
                    .AppendLine("\" for this bucket.");
            }
        }
        builder.AppendLine();
        builder.AppendLine("Each workItem MUST cite the listed evidenceIds. If two buckets share");
        builder.AppendLine("evidence, produce two separate work items with the same evidenceId(s).");
        builder.AppendLine("When a bucket names a REQUIRED agentOrSkill, using any other skill on");
        builder.AppendLine("that workItem is a rule 16.5 violation, even if the other skill is in");
        builder.AppendLine("availableSkills. Do NOT substitute build/add-arm64-target for a python/*");
        builder.AppendLine("bucket.");
    }

    private static void AppendAllowedEvidenceIds(
        StringBuilder builder, RepositoryAssessmentV1 assessment)
    {
        var ids = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var dep in assessment.Dependencies)
        {
            if (!string.IsNullOrWhiteSpace(dep.EvidenceId)) ids.Add(dep.EvidenceId);
        }
        foreach (var f in assessment.CodeFindings)
        {
            if (!string.IsNullOrWhiteSpace(f.EvidenceId)) ids.Add(f.EvidenceId);
        }
        if (!string.IsNullOrWhiteSpace(assessment.BuildFindings.EvidenceId))
        {
            ids.Add(assessment.BuildFindings.EvidenceId);
        }
        foreach (var u in assessment.Unknowns)
        {
            if (u.EvidenceIds is null) continue;
            foreach (var eid in u.EvidenceIds)
            {
                if (!string.IsNullOrWhiteSpace(eid)) ids.Add(eid);
            }
        }

        builder.AppendLine();
        builder.AppendLine();
        builder.AppendLine("=== ALLOWED evidenceId VALUES (per rule 6) ===");
        builder.AppendLine("These are the ONLY evidenceIds that exist in this assessment. Any");
        builder.AppendLine("plan citing an evidenceId not in this list will be rejected with HTTP");
        builder.AppendLine("422 planner.plan.missingEvidence. Do NOT invent or guess; if no");
        builder.AppendLine("evidence supports a field, use an empty evidenceIds array.");
        builder.AppendLine();

        if (ids.Count == 0)
        {
            builder.AppendLine("  (none)");
            return;
        }

        foreach (var id in ids)
        {
            builder.Append("  - ").AppendLine(id);
        }
    }

    // Renders the per-skill supportedInputs / supportedOutputs enumeration so the model can
    // populate workItems[].inputs and workItems[].expectedOutputs from these lists rather than
    // guessing (a common failure mode: pasting evidenceIds where skill I/O is expected, which
    // fails PlanSafetyValidator.ValidateSkillIo with planner.plan.skillIoMismatch).
    private static void AppendSkillIoAllowlist(
        StringBuilder builder, RepositoryAssessmentV1 assessment)
    {
        if (assessment.AvailableSkills.Count == 0)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine();
        builder.AppendLine("=== ALLOWED workItems[] INPUT/OUTPUT VALUES (per rule 6.5) ===");
        builder.AppendLine("For every workItems[].agentOrSkill you reference, workItems[].inputs and");
        builder.AppendLine("workItems[].expectedOutputs MUST be subsets of that skill's declared");
        builder.AppendLine("supportedInputs and supportedOutputs listed below. Any other string will");
        builder.AppendLine("be rejected with HTTP 422 planner.plan.skillIoMismatch.");
        builder.AppendLine();
        builder.AppendLine("These are NOT evidenceIds. evidenceIds go into workItems[].evidenceIds,");
        builder.AppendLine("never into workItems[].inputs.");
        builder.AppendLine();

        foreach (var skill in assessment.AvailableSkills)
        {
            if (string.IsNullOrEmpty(skill.Name)) continue;

            builder.Append("  ").AppendLine(skill.Name);
            var inputs = skill.SupportedInputs ?? Array.Empty<string>();
            var outputs = skill.SupportedOutputs ?? Array.Empty<string>();

            builder.Append("    supportedInputs  : ");
            if (inputs.Count == 0)
            {
                builder.AppendLine("(none — use an empty inputs array)");
            }
            else
            {
                builder.AppendLine(string.Join(", ", inputs));
            }

            builder.Append("    supportedOutputs : ");
            if (outputs.Count == 0)
            {
                builder.AppendLine("(none — use an empty expectedOutputs array)");
            }
            else
            {
                builder.AppendLine(string.Join(", ", outputs));
            }
        }
    }

    private const string SystemPrompt = """
        You are the ARM Migration Assist planner. You produce a MigrationPlanV1
        JSON document that recommends a Windows on Arm migration strategy for
        the supplied repository assessment.

        Hard rules:

        1. Output exactly one JSON object. Nothing else. No prose, no code
           fences, no Markdown. Your entire response must parse as JSON.
        2. Top-level keys MUST be exactly these 21, no additions and no
           omissions: schemaVersion, planId, assessmentId, generatedAt,
           modelProvenance, scoreDigest, corpusVersion, recommendedPath,
           confidence, executiveSummary, scoreInterpretation, facts,
           inferences, alternatives, workItems, missingSkills,
           validationPlan, risks, unknowns, requiredApprovals,
           reusableOutputs.
        3. Copy assessmentId, scoreDigest, and corpusVersion byte-for-byte
           from the input. schemaVersion is exactly "1.0". modelProvenance
           MUST use the exact provider/name/version values supplied in the
           "REQUIRED modelProvenance VALUES" section of the user prompt.
        4. recommendedPath is one of: native-arm64, arm64ec, staged, winui3,
           insufficient-evidence. confidence is one of: high, medium, low.
        5. RECOMMENDATION DISPATCH. Evaluate these in order and stop at the
           first match to pick recommendedPath. Do NOT stop at
           "insufficient-evidence" unless the band literally is
           "insufficient-evidence".

           STEP 5a — pick recommendedPath:
             (i)   deterministicScore.band == "insufficient-evidence"
                     -> recommendedPath = "insufficient-evidence"
             (ii)  deterministicScore.capsApplied contains capId
                   "required-unsupported-driver-le-30"
                     -> recommendedPath = "staged"
             (iii) deterministicScore.capsApplied contains capId
                   "required-x64-only-native-le-40"
                     -> recommendedPath = "arm64ec"
             (iv)  deterministicScore.capsApplied contains capId
                   "no-arm64-or-arm64ec-target-le-60"
                     -> recommendedPath = "native-arm64"
             (v)   deterministicScore.band == "ready-or-minor-changes"
                     -> recommendedPath = "native-arm64"
             (vi)  deterministicScore.band == "moderate-migration"
                     -> recommendedPath = "native-arm64"
             (vii) deterministicScore.band == "significant-remediation"
                     -> recommendedPath = "staged"
             (viii)deterministicScore.band == "blocked-or-major-redesign"
                     -> recommendedPath = "arm64ec"

           STEP 5b — pick confidence:
             (i)   recommendedPath == "insufficient-evidence" -> "low"
             (ii)  deterministicScore.provisional == true     -> "low"
             (iii) deterministicScore.band == "ready-or-minor-changes"
                                                              -> "high"
             (iv)  deterministicScore.band == "moderate-migration"
                                                              -> "medium"
             (v)   otherwise                                  -> "low"

           A "provisional" score is a signal that the deterministic score is
           complete enough to place a numeric band but had one weak input;
           it is NOT a reason to switch recommendedPath to
           "insufficient-evidence". Only band == "insufficient-evidence"
           forces that path.

           SERVER ENFORCEMENT. The server computes the expected
           recommendedPath from steps 5a and 5b and will reject the plan
           with HTTP 422 planner.plan.recommendationInconsistent if your
           recommendedPath disagrees. There is no retry; you get one
           attempt per assessment. Follow the dispatch mechanically.
        6. Cite only evidenceId values that appear in the assessment. Cite
           only guidanceId values that appear in guidanceIndex. Cite only
           skill names that appear in availableSkills; anything else belongs
           in missingSkills.
        6.5. workItems[].inputs and workItems[].expectedOutputs MUST be
             subsets of the referenced skill's declared supportedInputs and
             supportedOutputs. See the "ALLOWED workItems[] INPUT/OUTPUT
             VALUES" section for the exact per-skill lists. Do NOT paste
             evidenceIds, file paths, or human-readable descriptions here;
             those belong in workItems[].evidenceIds, not in inputs/outputs.
             Empty arrays are permitted when nothing applies. The server
             rejects any other value with HTTP 422
             planner.plan.skillIoMismatch.
        7. Never invent files, dependencies, or CI jobs. If unsure, add a
           PlanUnknown entry instead of guessing.
        8. Every workItem sets approvalRequired to true and has at least one
           acceptanceTests entry. Never propose repository writes, shell
           commands, publishing, commits, or pull requests.
        9. Nested id patterns are strict:
              workItems[].id            matches ^wi-[a-z0-9-]{2,60}$
              workItems[].dependencies  match the same wi- pattern
              acceptanceTests[].id      matches ^at-[a-z0-9-]{2,60}$
              validationPlan.*[].id     matches ^vc-[a-z0-9-]{2,60}$
              risks[].id                matches ^rk-[a-z0-9-]{2,60}$
              unknowns[].id             matches ^uk-[a-z0-9-]{2,60}$
              requiredApprovals[].approvalId matches ^ap-[a-z0-9-]{2,60}$
              planId                    matches ^[A-Za-z0-9_-]{3,64}$
        10. validationPlan MUST have exactly these keys, no more, no fewer:
              targetDevices (>= 1 from: snapdragon-x-series, nvidia-rtx-spark,
              arm64-vm, arm64-generic-hardware, x64-baseline-for-comparison,
              other), buildChecks, functionalChecks, reliabilityChecks,
              performanceChecks, powerChecks, offlineChecks,
              accessibilityChecks, windowsExperienceChecks. Empty arrays are
              allowed but every key must be present.
        11. When recommendedPath is not "insufficient-evidence" and
            deterministicScore.majorBlockers is non-empty, workItems MUST
            contain at least one entry per blocker. Each such work item
            cites the blocker's triggering evidenceIds.
        12. When recommendedPath is not "insufficient-evidence",
            validationPlan MUST contain at least one entry across
            buildChecks, functionalChecks, and performanceChecks combined.
        13. alternatives[] MUST contain an entry whose "path" equals
            recommendedPath and whose "disposition" is "viable". Other
            considered paths appear alongside with disposition "rejected"
            or "deferred".
        14. scoreInterpretation MUST paraphrase or lift verbatim the string
            in deterministicScore.scoreSummary. That string is the
            deterministic scorer's authoritative one-paragraph summary; do
            not contradict it. You may add one or two sentences of
            plain-language context, but do not invent conclusions the score
            did not reach.
        15. deterministicScore renamed its own confidence field to
            "evidenceCompleteness" (with numeric "evidenceCompletenessScore"
            in [0,1]). It measures how complete the input evidence is, NOT
            how sure the recommendation is. The plan-level confidence you
            emit is decided by step 5 above and is a separate value.
        16. Skill honesty. workItems[].agentOrSkill MUST reference either a
            name that appears in assessment.availableSkills[] OR a
            proposedName you declare in plan.missingSkills[]. Never invent
            skill names in workItems that are not in one of those two lists.
            The server will reject with HTTP 422
            planner.plan.missingSkill if any workItem cites an unknown
            skill. When you need capabilities Feature 1 has not offered
            yet (for example: dependency-management, ci-management,
            packaging), declare each as a MissingSkill entry with purpose,
            required inputs, expected outputs, justification, and
            evidenceIds, and then cite the same proposedName from any
            workItem that would use it.
        16.5. Project-type routing. Before choosing a skill, look at
              assessment.technology.languages and
              assessment.technology.buildSystems.

              PYTHON PROJECTS (technology.languages contains "python"):
                - `build/add-arm64-target` DOES NOT APPLY. There is no
                  MSBuild project to add ARM64 to. Do not cite it in a
                  workItem and do not add it to missingSkills[] — it is
                  runnable in the catalog but not applicable here.
                  Same for `build/add-arm64ec-target`,
                  `packaging/add-arm64-msix`, and any other MSBuild-oriented
                  skill.
                - The primary migration work items are the Python audit
                  skills. When they appear in assessment.availableSkills[],
                  cite them from workItems that address these concerns:
                    * `python/native-wheel-audit` — for every Python repo
                      with at least one pypi dependency. Addresses "which
                      pip packages need a win_arm64 wheel or a source
                      build". Inputs from the skill's supportedInputs
                      list (requirements.txt, pyproject.toml).
                    * `python/pytorch-arm64-wheel-audit` — when the
                      dependencies include torch, torchvision, torchaudio,
                      or torch-directml. Addresses "which torch pin is
                      viable on WoA".
                    * `python/cuda-to-directml-audit` — when the
                      dependencies include torch or the source is expected
                      to use CUDA. Addresses "which files/lines need to
                      switch off CUDA".
                    * `python/pip-constraints-arm64-scaffold` — when a
                      requirements.txt is present. Addresses "how does
                      the reviewer pin ARM64 wheels without editing
                      requirements.txt".
                - `pipeline/github-actions-arm64-job` still applies
                  because CI matrix additions are language-agnostic. Keep
                  it if the assessment shows github-actions.

              .NET / C++ PROJECTS (build systems contain msbuild, cmake,
              or the tree has .csproj/.vcxproj):
                - `build/add-arm64-target` and `pipeline/*` are primary.
                - `python/*` skills DO NOT APPLY unless technology.languages
                  also contains "python" AND a pypi dependency is declared.

              Never use missingSkills[] as a workaround for
              "the runnable skill I want to cite isn't listed in
              availableSkills for this repo". If a skill is in the catalog
              as runnable but not offered by availableSkills, that means
              the assessment did not surface the inputs the skill needs —
              route into a different available skill instead.
        17. Risk depth. Produce enough risks that a reviewer can act on
            them. Concretely:
              - At least one Risk per entry in
                deterministicScore.majorBlockers (cite the same evidenceIds
                the blocker points at).
              - At least one Risk per required dependency with
                architectureStatus == "emulation-only" or "blocked"
                (evidenceIds should cite the dependency's evidenceId).
              - Include severity and a concrete mitigation for each.
              - Do not exceed 20 risks; consolidate related risks when the
                mitigation is the same.
        18. Alternative-level effort and risk. Each entry in alternatives[]
            SHOULD include the optional "estimatedEffort" and "risk". If
            included they MUST come from these closed sets exactly (no
            other strings, no null):
              estimatedEffort ∈ { "small", "medium", "large", "unknown" }
              risk            ∈ { "low", "medium", "high", "critical" }
            Same closed sets apply to workItems[].estimatedEffort and
            workItems[].risk. Any other value fails schema validation and
            the plan will be rejected.
        19. Closed sets you MUST NOT drift from:
              unknowns[].requiredSkill : either null OR a SkillReference
                  matching ^[a-z][a-z0-9-]*(/[a-z][a-z0-9-]*)*$
                  (kebab-case, optional "/" namespaces). Never a
                  human-readable description like "Windows Experience
                  Analysis" or a value with uppercase or spaces.
              risks[].severity        ∈ { "low", "medium", "high", "critical" }
              workItems[].priority    ∈ { "P0", "P1", "P2" }
              alternatives[].disposition ∈ { "rejected", "deferred", "viable" }
        20. workItem granularity. Prefer several focused work items over one
            broad item. Aim for 4-10 workItems on moderate migrations, 2-4
            on small ones, scaling with the assessment's blocker and
            dependency counts. Produce, at minimum:
              - one workItem per required dependency whose
                architectureStatus is "blocked" or "emulation-only"; the
                title MUST name the dep;
              - one workItem per top-level build/CI/packaging change that
                the score identifies (add-arm64-target, add-arm64-ci-job,
                add-arm64-packaging, add-arm64-tests) — only for changes
                the score's deductions actually surface AND only when the
                project type in rule 16.5 supports them;
              - one workItem per critical code finding; the title MUST
                name the ruleId and file;
              - for Python projects (per rule 16.5) one workItem per
                applicable python/* audit skill listed there.
            Each workItem's objective MUST be 1-4 concrete sentences
            naming what changes, in which files or configs, and what shape
            the output takes. Each MUST include >= 1 input path drawn from
            the assessment and >= 1 named expected output
            (patch, workflow-yaml, wheel-build-recipe, packaging-manifest,
            doc-page, test-file, report, etc.). Each MUST include >= 2
            acceptanceTests with distinct expectedOutcomes (typically a
            build check plus a functional check; add a perf or reliability
            check when relevant). Do NOT combine multiple deps or multiple
            file categories into a single "setup" work item.

        Reference: a valid MigrationPlanV1 looks EXACTLY like the JSON below.
        Only the field VALUES change per assessment; the KEYS and structure
        never change.

        {
          "schemaVersion": "1.0",
          "planId": "plan-example-001",
          "assessmentId": "assessment-example-001",
          "generatedAt": "2026-09-15T12:00:00Z",
          "modelProvenance": {
            "provider": "foundry",
            "name": "phi-4",
            "version": "7.0.0"
          },
          "scoreDigest": "0000000000000000000000000000000000000000000000000000000000000000",
          "corpusVersion": "2026-09-15.1",
          "recommendedPath": "native-arm64",
          "confidence": "medium",
          "executiveSummary": "All required dependencies are ARM64-ready and the source is architecture-agnostic. The gap is exclusively in build configuration and CI. Adding ARM64 targets and an ARM64 CI job is the shortest path.",
          "scoreInterpretation": "Dependency and code dimensions score high; build readiness is the pulled-down dimension because only x64 targets exist. No blocker caps applied.",
          "facts": [
            {
              "statement": "The project declares only PlatformTarget x64.",
              "evidenceIds": ["build-example-01"],
              "guidanceIds": ["add-arm-support-01"]
            }
          ],
          "inferences": [
            {
              "statement": "Adding an ARM64 configuration is low-risk because the code is managed .NET with no native interop.",
              "evidenceIds": ["dep-example-01", "build-example-01"],
              "guidanceIds": ["add-arm-support-01"],
              "confidence": 0.85
            }
          ],
          "alternatives": [
            {
              "path": "native-arm64",
              "disposition": "viable",
              "rationale": "Preferred: cheapest path for a managed .NET app with no x64-only native dependencies.",
              "evidenceIds": ["dep-example-01"],
              "guidanceIds": ["add-arm-support-01"],
              "estimatedEffort": "small",
              "risk": "low"
            },
            {
              "path": "arm64ec",
              "disposition": "rejected",
              "rationale": "Arm64EC only pays off when there is x64-only native code to preserve; this app has none.",
              "evidenceIds": ["dep-example-01"],
              "guidanceIds": ["arm64ec-overview-01"],
              "estimatedEffort": "large",
              "risk": "medium"
            }
          ],
          "workItems": [
            {
              "id": "wi-add-arm64-target",
              "sequence": 1,
              "priority": "P0",
              "title": "Add ARM64 configuration to Example.csproj",
              "objective": "Add an ARM64 PlatformTarget entry alongside the existing x64 target in src/Example/Example.csproj. Produce a patch that leaves the x64 configuration intact so both can be built side by side.",
              "agentOrSkill": "build/add-arm64-target",
              "inputs": ["src/Example/Example.csproj"],
              "expectedOutputs": ["patch"],
              "dependencies": [],
              "evidenceIds": ["build-example-01"],
              "guidanceIds": ["add-arm-support-01"],
              "acceptanceTests": [
                {
                  "id": "at-arm64-build-succeeds",
                  "description": "The ARM64/Release configuration builds without warnings.",
                  "expectedOutcome": "Zero MSBuild errors or warnings when building ARM64/Release."
                },
                {
                  "id": "at-x64-build-still-succeeds",
                  "description": "The existing x64 configuration still builds.",
                  "expectedOutcome": "Zero MSBuild errors when building x64/Release."
                }
              ],
              "approvalRequired": true,
              "estimatedEffort": "small",
              "risk": "low"
            },
            {
              "id": "wi-add-arm64-ci-job",
              "sequence": 2,
              "priority": "P0",
              "title": "Add windows-arm64 CI job to ci.yml",
              "objective": "Add a windows-arm64 runner job to .github/workflows/ci.yml that mirrors the existing x64 job: restore, build ARM64/Release, run unit tests, and upload the arm64 binaries as an artifact.",
              "agentOrSkill": "build/add-ci-job",
              "inputs": [".github/workflows/ci.yml"],
              "expectedOutputs": ["workflow-yaml"],
              "dependencies": ["wi-add-arm64-target"],
              "evidenceIds": ["build-example-01"],
              "guidanceIds": ["add-arm-support-01"],
              "acceptanceTests": [
                {
                  "id": "at-ci-arm64-job-passes",
                  "description": "The new ARM64 CI job completes successfully.",
                  "expectedOutcome": "GitHub Actions reports success for the windows-arm64 job on the main branch."
                },
                {
                  "id": "at-ci-tests-run-on-arm64",
                  "description": "Unit tests run under the ARM64 job.",
                  "expectedOutcome": "Test summary shows non-zero passing tests and zero failures on ARM64."
                }
              ],
              "approvalRequired": true,
              "estimatedEffort": "medium",
              "risk": "low"
            },
            {
              "id": "wi-port-dep-example-native",
              "sequence": 3,
              "priority": "P1",
              "title": "Port dep-example-native to ARM64 via source build",
              "objective": "The dep-example-native package publishes only x64 binaries. Add a source-build recipe under scripts/build-deps/example-native/build.ps1 that compiles the ARM64 artifact from upstream sources, vendors it into the internal feed, and pins the version in the project's dependency manifest.",
              "agentOrSkill": "dependency/source-build",
              "inputs": ["scripts/build-deps/", "src/Example/Example.csproj"],
              "expectedOutputs": ["build-recipe", "dependency-patch"],
              "dependencies": [],
              "evidenceIds": ["dep-example-01"],
              "guidanceIds": ["add-arm-support-01"],
              "acceptanceTests": [
                {
                  "id": "at-dep-built-arm64",
                  "description": "The arm64 artifact for dep-example-native builds cleanly from source.",
                  "expectedOutcome": "The build script produces an arm64-tagged artifact under the expected output directory."
                },
                {
                  "id": "at-dep-loads-on-arm64",
                  "description": "The app loads dep-example-native on an ARM64 device.",
                  "expectedOutcome": "On startup, the process loads the arm64 artifact and completes initialization without a DllNotFoundException or BadImageFormatException."
                }
              ],
              "approvalRequired": true,
              "estimatedEffort": "large",
              "risk": "high"
            },
            {
              "id": "wi-add-arm64-packaging",
              "sequence": 4,
              "priority": "P2",
              "title": "Produce ARM64 installer bundle",
              "objective": "Extend the packaging under packaging/Product.wxs to emit an ARM64 payload alongside the existing x64 payload and produce a dual-arch bundle installer that selects the payload at install time based on the host machine architecture.",
              "agentOrSkill": "packaging/add-arm64",
              "inputs": ["packaging/Product.wxs", "packaging/Bundle.wxs"],
              "expectedOutputs": ["packaging-patch", "installer-artifact"],
              "dependencies": ["wi-add-arm64-target", "wi-port-dep-example-native"],
              "evidenceIds": ["build-example-01"],
              "guidanceIds": ["msix-arm64-packaging-01"],
              "acceptanceTests": [
                {
                  "id": "at-installer-builds-dual-arch",
                  "description": "The installer build produces a dual-arch bundle.",
                  "expectedOutcome": "Packaging output contains both x64 and arm64 payloads inside a single bundle .exe."
                },
                {
                  "id": "at-installer-runs-on-arm64",
                  "description": "The bundle installs cleanly on an ARM64 device.",
                  "expectedOutcome": "Install completes with exit code 0 on a Snapdragon X test device and the installed binary reports arm64 at runtime."
                }
              ],
              "approvalRequired": true,
              "estimatedEffort": "medium",
              "risk": "medium"
            }
          ],
          "missingSkills": [
            {
              "proposedName": "assessment/accessibility-audit",
              "purpose": "Grade WinForms and XAML accessibility affordances against Windows UX guidelines.",
              "requiredInputs": ["repository"],
              "expectedOutputs": ["accessibility-report"],
              "justification": "The assessment did not evaluate accessibility, so the plan cannot recommend the WinUI 3 modernization alternative with confidence.",
              "evidenceIds": [],
              "writeAccess": false
            }
          ],
          "validationPlan": {
            "targetDevices": ["snapdragon-x-series", "arm64-vm"],
            "buildChecks": [
              {
                "id": "vc-arm64-release-builds",
                "description": "Full solution builds on ARM64/Release.",
                "expectedOutcome": "Build succeeds with zero errors."
              }
            ],
            "functionalChecks": [
              {
                "id": "vc-smoke-launches",
                "description": "Application launches and reaches the main window on a Snapdragon X device.",
                "expectedOutcome": "Main window appears within five seconds; no crash dialog."
              }
            ],
            "reliabilityChecks": [],
            "performanceChecks": [],
            "powerChecks": [],
            "offlineChecks": [],
            "accessibilityChecks": [],
            "windowsExperienceChecks": []
          },
          "risks": [
            {
              "id": "rk-installer-arm64",
              "description": "The installer currently produces only an x64 payload.",
              "severity": "medium",
              "mitigation": "Add an ARM64 installer configuration and produce a dual-arch bundle before release.",
              "evidenceIds": ["build-example-01"],
              "guidanceIds": ["add-arm-support-01"]
            }
          ],
          "unknowns": [
            {
              "id": "uk-accessibility",
              "description": "Accessibility affordances of the current UI were not evaluated.",
              "requiredSkill": "assessment/accessibility-audit",
              "evidenceIds": []
            }
          ],
          "requiredApprovals": [
            {
              "approvalId": "ap-build-and-ci-changes",
              "summary": "Human approval before modifying project files, CI workflows, or packaging.",
              "workItemIds": ["wi-add-arm64-target", "wi-add-arm64-ci-job", "wi-add-arm64-packaging"]
            },
            {
              "approvalId": "ap-dependency-rebuild",
              "summary": "Human approval before adding source-build recipes for third-party dependencies.",
              "workItemIds": ["wi-port-dep-example-native"]
            }
          ],
          "reusableOutputs": [
            {
              "name": "arm64-csproj-config-template",
              "kind": "template",
              "description": "The multi-arch csproj snippet added by this plan can be reused by other managed .NET apps."
            }
          ]
        }
        """;
}
