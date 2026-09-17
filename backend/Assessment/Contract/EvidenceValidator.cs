namespace ArmMigrationAssist.Api.Assessment.Contract;

/// <summary>
/// Enforces the cross-record rules the schema documents but JSON Schema cannot express:
/// unique evidence IDs, evidence-reference resolution, and filesScanned &lt;= filesTotal.
/// The Feature 2 planner runs the same validation before consuming an assessment.
/// </summary>
public static class EvidenceValidator
{
    public static IReadOnlyList<string> Validate(RepositoryAssessmentV1 doc)
    {
        var errors = new List<string>();

        // 1. Unique evidence IDs across all finding records.
        var ids = new List<string>();
        ids.AddRange(doc.Dependencies.Select(d => d.EvidenceId));
        ids.AddRange(doc.CodeFindings.Select(c => c.EvidenceId));
        ids.Add(doc.BuildFindings.EvidenceId);

        var duplicates = ids.GroupBy(x => x).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        foreach (var dup in duplicates)
            errors.Add($"Duplicate evidenceId '{dup}'.");

        // 2. filesScanned <= filesTotal.
        if (doc.ScanCoverage.FilesScanned > doc.ScanCoverage.FilesTotal)
            errors.Add($"filesScanned ({doc.ScanCoverage.FilesScanned}) exceeds filesTotal ({doc.ScanCoverage.FilesTotal}).");

        // 3. Evidence-reference resolution: every unknown evidenceId must resolve.
        var known = new HashSet<string>(ids);
        foreach (var u in doc.Unknowns)
        {
            if (u.EvidenceIds is null) continue;
            foreach (var refId in u.EvidenceIds)
                if (!known.Contains(refId))
                    errors.Add($"Unknown references unresolved evidenceId '{refId}'.");
        }

        // 4. Evidence array caps (schema maxItems) — the planner rejects oversize arrays and
        //    an oversize payload also blows its token budget, so catch it before the round-trip.
        CheckEvidenceCap(errors, "buildFindings.evidence", doc.BuildFindings.Evidence, 40);
        CheckEvidenceCap(errors, "windowsExperience.evidence", doc.WindowsExperience.Evidence, 40);
        foreach (var d in doc.Dependencies)
            CheckEvidenceCap(errors, $"dependencies[{d.EvidenceId}].evidence", d.Evidence, 20);
        foreach (var c in doc.CodeFindings)
            CheckEvidenceCap(errors, $"codeFindings[{c.EvidenceId}].evidence", c.Evidence, 20);

        // 5. Evidence oneOf: each item must reference EXACTLY ONE of path/artifact.
        foreach (var (label, item) in AllEvidence(doc))
        {
            var hasPath = !string.IsNullOrWhiteSpace(item.Path);
            var hasArtifact = !string.IsNullOrWhiteSpace(item.Artifact);
            if (hasPath == hasArtifact)
                errors.Add($"Evidence in {label} must have exactly one of path/artifact (has {(hasPath ? "both" : "neither")}).");
        }

        return errors;
    }

    private static void CheckEvidenceCap(List<string> errors, string label, IReadOnlyCollection<EvidenceV1> evidence, int max)
    {
        if (evidence.Count > max)
            errors.Add($"{label} has {evidence.Count} items, exceeds maxItems {max}.");
    }

    private static IEnumerable<(string Label, EvidenceV1 Item)> AllEvidence(RepositoryAssessmentV1 doc)
    {
        foreach (var e in doc.BuildFindings.Evidence) yield return ("buildFindings.evidence", e);
        foreach (var e in doc.WindowsExperience.Evidence) yield return ("windowsExperience.evidence", e);
        foreach (var d in doc.Dependencies)
            foreach (var e in d.Evidence) yield return ($"dependencies[{d.EvidenceId}].evidence", e);
        foreach (var c in doc.CodeFindings)
            foreach (var e in c.Evidence) yield return ($"codeFindings[{c.EvidenceId}].evidence", e);
    }
}
