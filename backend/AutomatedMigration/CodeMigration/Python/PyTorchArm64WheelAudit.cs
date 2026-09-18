using System.Text;
using AutomatedMigration.Generators;
using AutomatedMigration.Models;

namespace AutomatedMigration.CodeMigration.Python;

// Audits torch / torchvision / torchaudio version pins against the Windows on
// Arm wheel-status corpus entry. Emits a report the reviewer can act on; does
// not modify requirements.txt. Grounded in guidance pytorch-woa-status-01.
public sealed class PyTorchArm64WheelAudit : IMigrationGenerator
{
    private static readonly HashSet<string> TorchPackages = new(StringComparer.OrdinalIgnoreCase)
    {
        "torch", "torchvision", "torchaudio", "torch-directml"
    };

    private const string ReportPath = ".arm-migration/reports/pytorch-arm64-wheel-audit.md";

    public GeneratedPatch? Generate(WorkItem workItem, MigrationContext context)
    {
        var deps = PipRequirementsParser.ScanRepo(context.RepoPath);
        var torch = deps
            .Where(d => TorchPackages.Contains(d.NormalizedName))
            .ToList();

        if (torch.Count == 0)
            return null;

        var body = BuildReport(torch);
        return new GeneratedPatch(UnifiedDiff.NewFile(ReportPath, body));
    }

    private static string BuildReport(IReadOnlyList<PipRequirementsParser.PipDependency> torch)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# PyTorch ARM64 wheel audit");
        sb.AppendLine();
        sb.AppendLine("Grounded in [guidance `pytorch-woa-status-01`](../../knowledge/windows-on-arm/snippets/pytorch-woa-status-01.md).");
        sb.AppendLine();
        sb.AppendLine("| Package | Declared pin | Location | Windows-on-Arm status |");
        sb.AppendLine("| ------- | ------------ | -------- | --------------------- |");
        foreach (var dep in torch)
        {
            var status = ClassifyPin(dep);
            var pin = dep.VersionSpec is null ? "(unpinned)" : dep.VersionSpec;
            sb.Append("| `").Append(dep.NormalizedName).Append("` | ")
              .Append(pin).Append(" | `").Append(dep.SourceFile).Append(':').Append(dep.LineNumber)
              .Append("` | ").Append(status).AppendLine(" |");
        }
        sb.AppendLine();
        sb.AppendLine("## What this means");
        sb.AppendLine();
        sb.AppendLine("- `torch`, `torchvision`, and `torchaudio` publish native Windows-ARM64 wheels only for recent versions (see corpus entry `pytorch-woa-status-01` for the version window that shipped `win_arm64` wheels).");
        sb.AppendLine("- The wheels are **CPU-only**. There is no NVIDIA CUDA on Windows on Arm. Any `.cuda()` / `.to(\"cuda\")` call sites need one of:");
        sb.AppendLine("  1. `torch-directml` (`pip install torch-directml`) — routes tensor ops through DirectML on Snapdragon / Adreno.");
        sb.AppendLine("  2. `onnxruntime-directml` or `onnxruntime-qnn` (see guidance `onnxruntime-arm64-01`) — export the model and run inference outside PyTorch.");
        sb.AppendLine("  3. Plain CPU fallback — remove `.cuda()` calls and let ops run on the Snapdragon CPU cores.");
        sb.AppendLine();
        sb.AppendLine("## Suggested actions");
        sb.AppendLine();
        sb.AppendLine("1. Confirm the pinned torch version has a `win_arm64` wheel published on PyPI. If unpinned, add an ARM64-scoped constraint. See the sibling audit `python/pip-constraints-arm64-scaffold` for a starter file.");
        sb.AppendLine("2. If CUDA calls exist, run the sibling audit `python/cuda-to-directml-audit` to enumerate their locations before choosing an execution backend.");
        sb.AppendLine("3. Do **not** treat this report as a promise that the pinned version works. A `win_arm64` wheel existing on PyPI is a necessary condition, not a sufficient one.");
        return sb.ToString();
    }

    private static string ClassifyPin(PipRequirementsParser.PipDependency dep)
    {
        // Deliberately conservative: report the pin as-is and defer the go/no-go to a reviewer + PyPI query.
        // Anything else would be a fabricated claim about specific wheel availability.
        if (dep.VersionSpec is null)
            return "Unpinned — pin required before an ARM64 build can be reproduced.";
        return "Verify `win_arm64` wheel exists on PyPI for this pin.";
    }
}
