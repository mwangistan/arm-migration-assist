using System.Text;
using System.Text.Json;
using MigrationPlanner.Application.Abstractions;
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
        PlannerRetryHint? retryHint = null)
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

        if (retryHint is not null)
        {
            builder.AppendLine();
            builder.AppendLine();
            builder.AppendLine("=== RETRY CORRECTION ===");
            builder.AppendLine("Your previous attempt was rejected by the server. Reproduce the plan");
            builder.AppendLine("EXACTLY, changing only the fields listed below plus anything strictly");
            builder.AppendLine("required to keep alternatives[] internally consistent.");
            builder.AppendLine();
            builder.AppendLine($"Previous recommendedPath : \"{retryHint.PreviousRecommendedPath}\"");
            builder.AppendLine($"Previous confidence      : \"{retryHint.PreviousConfidence}\"");
            builder.AppendLine($"Required recommendedPath : \"{retryHint.ExpectedRecommendedPath}\"");
            builder.AppendLine($"Required confidence      : \"{retryHint.ExpectedConfidence}\"");
            builder.AppendLine($"Server diagnostic        : {retryHint.Diagnostic}");
            builder.AppendLine();
            builder.AppendLine("Rules for the retry:");
            builder.AppendLine("  1. Set recommendedPath and confidence to the Required values above.");
            builder.AppendLine("  2. alternatives[] MUST contain an entry with path equal to the");
            builder.AppendLine("     Required recommendedPath and disposition=\"viable\". Change the");
            builder.AppendLine("     previous recommendedPath's alternative entry to disposition=");
            builder.AppendLine("     \"rejected\" or \"deferred\" as appropriate.");
            builder.AppendLine("  3. Keep facts, inferences, workItems, validationPlan, risks,");
            builder.AppendLine("     unknowns, missingSkills, requiredApprovals, and reusableOutputs");
            builder.AppendLine("     unchanged unless the change in recommendedPath makes an entry");
            builder.AppendLine("     internally inconsistent.");
            builder.AppendLine("  4. Output the corrected JSON only. No prose.");
        }

        return builder.ToString();
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
           always uses provider="foundry", name="phi-4", version="7.0.0".
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
              "guidanceIds": ["add-arm-support-01"]
            },
            {
              "path": "arm64ec",
              "disposition": "rejected",
              "rationale": "Arm64EC only pays off when there is x64-only native code to preserve; this app has none.",
              "evidenceIds": ["dep-example-01"],
              "guidanceIds": ["arm64ec-overview-01"]
            }
          ],
          "workItems": [
            {
              "id": "wi-add-arm64-target",
              "sequence": 1,
              "priority": "P0",
              "title": "Add ARM64 configuration to the csproj",
              "objective": "Add an ARM64 build configuration alongside the existing x64 target so the project produces native ARM64 binaries.",
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
                }
              ],
              "approvalRequired": true,
              "estimatedEffort": "small",
              "risk": "low"
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
              "approvalId": "ap-build-changes",
              "summary": "Human approval before modifying any project or CI files.",
              "workItemIds": ["wi-add-arm64-target"]
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
