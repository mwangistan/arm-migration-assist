using AutomatedMigration.Generators;
using AutomatedMigration.Models;

namespace AutomatedMigration.CodeMigration;

// Story 3.3 — the AI-backed code transformer. The planner says WHAT and WHERE
// (file + objective + evidence); this asks a model to draft the exact change as
// a unified diff. Output is review-only and approval-gated like every other
// generator. Skipped when no model is configured.
public sealed class CodePatcher : IMigrationGenerator
{
    private readonly IChatModel? _model;

    public CodePatcher(IChatModel? model) => _model = model;

    public GeneratedPatch? Generate(WorkItem workItem, MigrationContext context)
    {
        if (_model is null)
        {
            Console.WriteLine("    code-transformer skipped: set GITHUB_TOKEN to enable the AI transformer.");
            return null;
        }

        var target = workItem.Inputs?.FirstOrDefault();
        if (target is null)
            return null;
        var full = Path.Combine(context.RepoPath, target);
        if (!File.Exists(full))
            return null;

        var source = File.ReadAllText(full);
        var system =
            "You are a Windows on Arm migration assistant. You transform architecture-specific " +
            "C/C++ so it builds and runs on ARM64. Reply with ONLY a git-apply-compatible unified " +
            "diff for the single file provided. No prose, no markdown fences.";
        var user =
            $"File: {target}\n" +
            $"Objective: {workItem.Objective}\n" +
            $"Evidence: {string.Join(", ", workItem.EvidenceIds ?? new List<string>())}\n\n" +
            $"Current contents:\n{source}\n\n" +
            $"Return a minimal unified diff using paths a/{target} and b/{target}.";

        var raw = _model.Complete(system, user);
        var diff = ExtractDiff(raw);
        return string.IsNullOrWhiteSpace(diff) ? null : new GeneratedPatch(diff);
    }

    // Models sometimes wrap output in ```diff fences; strip them.
    private static string ExtractDiff(string text)
    {
        text = text.Trim();
        if (text.StartsWith("```"))
        {
            int firstNewline = text.IndexOf('\n');
            if (firstNewline >= 0)
                text = text[(firstNewline + 1)..];
            int fence = text.LastIndexOf("```", StringComparison.Ordinal);
            if (fence >= 0)
                text = text[..fence];
        }
        return text.Trim() + "\n";
    }
}
