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

        return errors;
    }
}
