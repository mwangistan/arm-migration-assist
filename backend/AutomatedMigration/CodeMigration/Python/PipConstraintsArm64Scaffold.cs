using System.Text;
using AutomatedMigration.Generators;
using AutomatedMigration.Models;

namespace AutomatedMigration.CodeMigration.Python;

// Emits a starter `constraints-arm64.txt` alongside a short README explaining
// how to use it with `pip install -c constraints-arm64.txt -r requirements.txt`.
// The scaffold intentionally leaves the pins empty; it names the packages the
// reviewer must resolve manually and points at the corresponding audit
// reports. Nothing here claims a specific pinned version works on ARM64.
public sealed class PipConstraintsArm64Scaffold : IMigrationGenerator
{
    private const string ConstraintsPath = "constraints-arm64.txt";
    private const string ReportPath = ".arm-migration/reports/pip-constraints-arm64.md";

    public GeneratedPatch? Generate(WorkItem workItem, MigrationContext context)
    {
        var deps = PipRequirementsParser.ScanRepo(context.RepoPath);
        if (deps.Count == 0)
            return null;

        // A repo can already have this file (from an earlier run). If so, skip
        // rather than overwrite: F3 is single-pass and non-destructive.
        var existing = Path.Combine(context.RepoPath, ConstraintsPath);
        if (File.Exists(existing))
            return null;

        var constraints = BuildConstraints(deps);
        var report = BuildReport(deps);

        var sb = new StringBuilder();
        sb.Append(UnifiedDiff.NewFile(ConstraintsPath, constraints));
        sb.Append(UnifiedDiff.NewFile(ReportPath, report));
        return new GeneratedPatch(sb.ToString());
    }

    private static string BuildConstraints(IReadOnlyList<PipRequirementsParser.PipDependency> deps)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# constraints-arm64.txt");
        sb.AppendLine("#");
        sb.AppendLine("# Windows-on-Arm pin overrides. Use with:");
        sb.AppendLine("#   pip install -c constraints-arm64.txt -r requirements.txt");
        sb.AppendLine("#");
        sb.AppendLine("# All pins below are left empty on purpose. Fill each one after");
        sb.AppendLine("# verifying that a `win_arm64` wheel exists on PyPI (or after");
        sb.AppendLine("# arranging a source build). See:");
        sb.AppendLine("#   .arm-migration/reports/pytorch-arm64-wheel-audit.md");
        sb.AppendLine("#   .arm-migration/reports/python-native-wheel-audit.md");
        sb.AppendLine();

        var groups = deps
            .Select(d => (d.NormalizedName, d.VersionSpec))
            .OrderBy(x => x.NormalizedName, StringComparer.Ordinal)
            .ToList();
        foreach (var (name, spec) in groups)
        {
            var currentPin = spec is null ? "unpinned" : spec;
            sb.Append("# ").Append(name).Append(" (currently ").Append(currentPin).AppendLine(")");
            sb.Append("# ").Append(name).AppendLine("==");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static string BuildReport(IReadOnlyList<PipRequirementsParser.PipDependency> deps)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# constraints-arm64.txt scaffold");
        sb.AppendLine();
        sb.Append("A starter `constraints-arm64.txt` was added with ").Append(deps.Count).AppendLine(" placeholder entries. Every pin is intentionally left blank.");
        sb.AppendLine();
        sb.AppendLine("## Why blank");
        sb.AppendLine();
        sb.AppendLine("This runner cannot query PyPI at plan time, so it does not know which specific `<name>==<version>` combinations have a published `win_arm64` wheel. Emitting a pin without verifying it would fabricate readiness. The scaffold names the packages so a reviewer can fill them in after either:");
        sb.AppendLine();
        sb.AppendLine("- Running `pip index versions <name>` locally on an ARM64 Python environment and picking the highest that has a `win_arm64` wheel, or");
        sb.AppendLine("- Running `pip download <name>==<pin> --platform win_arm64 --only-binary=:all: --dest /tmp/wheels/` and confirming the download succeeds.");
        sb.AppendLine();
        sb.AppendLine("## How to use once populated");
        sb.AppendLine();
        sb.AppendLine("```powershell");
        sb.AppendLine("py -3 -m venv .venv-arm64");
        sb.AppendLine(".venv-arm64\\Scripts\\Activate.ps1");
        sb.AppendLine("pip install --upgrade pip");
        sb.AppendLine("pip install -c constraints-arm64.txt -r requirements.txt");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("The constraints file only affects the pip resolver — it does not change `requirements.txt`. `requirements.txt` still holds your canonical pins for x64.");
        sb.AppendLine();
        sb.AppendLine("Related audits:");
        sb.AppendLine("- `.arm-migration/reports/pytorch-arm64-wheel-audit.md`");
        sb.AppendLine("- `.arm-migration/reports/python-native-wheel-audit.md`");
        sb.AppendLine("- `.arm-migration/reports/cuda-to-directml-audit.md`");
        return sb.ToString();
    }
}
