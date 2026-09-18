using System.Text;
using System.Text.RegularExpressions;
using AutomatedMigration.Generators;
using AutomatedMigration.Models;

namespace AutomatedMigration.CodeMigration.Python;

// Scans Python source for CUDA usage. Windows on Arm has no NVIDIA CUDA, so
// each site listed here needs a routing decision: torch-directml, ONNX Runtime
// DirectML/QNN, or CPU fallback. This runner does not choose; it enumerates.
public sealed class CudaToDirectMlAudit : IMigrationGenerator
{
    private static readonly Regex[] CudaPatterns =
    [
        new("""\.to\s*\(\s*['"]cuda(?::\d+)?['"]""", RegexOptions.Compiled),
        new(@"\.cuda\s*\(", RegexOptions.Compiled),
        new("""torch\.device\s*\(\s*['"]cuda""", RegexOptions.Compiled),
        new(@"torch\.cuda\.", RegexOptions.Compiled),
        new(@"torch\.backends\.cudnn", RegexOptions.Compiled),
        new(@"@?custom_bwd|@?custom_fwd", RegexOptions.Compiled), // torch.cuda.amp APIs
        new(@"CUDA_VISIBLE_DEVICES", RegexOptions.Compiled),
        new(@"nvidia-smi\b", RegexOptions.Compiled),
    ];

    // Directories we skip when walking the tree. Keeps the audit fast and
    // avoids reporting inside vendored third-party trees.
    private static readonly HashSet<string> SkipDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".venv", "venv", "env", "node_modules", "__pycache__",
        "dist", "build", ".tox", ".mypy_cache", ".pytest_cache",
        ".arm-migration",
    };

    private const string ReportPath = ".arm-migration/reports/cuda-to-directml-audit.md";
    private const int MaxHitsPerFile = 20;
    private const int MaxFilesReported = 200;

    public GeneratedPatch? Generate(WorkItem workItem, MigrationContext context)
    {
        var hits = Scan(context.RepoPath);
        if (hits.Count == 0)
            return null;

        var body = BuildReport(hits);
        return new GeneratedPatch(UnifiedDiff.NewFile(ReportPath, body));
    }

    private static IReadOnlyList<Hit> Scan(string repoPath)
    {
        var hits = new List<Hit>();
        foreach (var file in EnumeratePythonFiles(repoPath))
        {
            string[] lines;
            try { lines = File.ReadAllLines(file); }
            catch { continue; }

            var fileHits = new List<(int Line, string Snippet)>();
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                foreach (var pat in CudaPatterns)
                {
                    if (pat.IsMatch(line))
                    {
                        fileHits.Add((i + 1, Truncate(line.Trim(), 160)));
                        break;
                    }
                }
                if (fileHits.Count >= MaxHitsPerFile)
                    break;
            }
            if (fileHits.Count == 0)
                continue;
            var relative = Path.GetRelativePath(repoPath, file).Replace('\\', '/');
            hits.Add(new Hit(relative, fileHits));
            if (hits.Count >= MaxFilesReported)
                break;
        }
        return hits;
    }

    private static IEnumerable<string> EnumeratePythonFiles(string root)
    {
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            IEnumerable<string> subs;
            try { subs = Directory.EnumerateDirectories(dir); }
            catch { continue; }
            foreach (var sub in subs)
            {
                var name = Path.GetFileName(sub);
                if (SkipDirs.Contains(name))
                    continue;
                stack.Push(sub);
            }
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(dir, "*.py"); }
            catch { continue; }
            foreach (var f in files)
                yield return f;
        }
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";

    private static string BuildReport(IReadOnlyList<Hit> hits)
    {
        var totalHits = hits.Sum(h => h.Lines.Count);
        var sb = new StringBuilder();
        sb.AppendLine("# CUDA → DirectML / ONNX Runtime audit");
        sb.AppendLine();
        sb.AppendLine("Grounded in [`pytorch-woa-status-01`](../../knowledge/windows-on-arm/snippets/pytorch-woa-status-01.md) and [`onnxruntime-arm64-01`](../../knowledge/windows-on-arm/snippets/onnxruntime-arm64-01.md).");
        sb.AppendLine();
        sb.Append("**Summary:** ").Append(totalHits).Append(" CUDA reference(s) across ")
          .Append(hits.Count).AppendLine(" file(s).");
        sb.AppendLine();
        sb.AppendLine("Windows on Arm has no NVIDIA CUDA. Every reference below needs one of the routing choices in the section that follows.");
        sb.AppendLine();
        sb.AppendLine("## Sites");
        sb.AppendLine();
        foreach (var hit in hits)
        {
            sb.Append("### `").Append(hit.File).Append("` (").Append(hit.Lines.Count).AppendLine(" hits)");
            sb.AppendLine();
            sb.AppendLine("| Line | Excerpt |");
            sb.AppendLine("| ---- | ------- |");
            foreach (var (line, snippet) in hit.Lines)
            {
                sb.Append("| ").Append(line).Append(" | `").Append(EscapeMd(snippet)).AppendLine("` |");
            }
            sb.AppendLine();
        }
        sb.AppendLine("## Routing options");
        sb.AppendLine();
        sb.AppendLine("Ordered by lowest code change first:");
        sb.AppendLine();
        sb.AppendLine("1. **`torch-directml`** — install `torch-directml` and replace `\"cuda\"` device strings with a DirectML device. Runs on Adreno/Snapdragon GPU; smallest diff. Coverage is not 100 % of the torch op set.");
        sb.AppendLine("2. **ONNX Runtime + DirectML EP** — export the model with `torch.onnx.export` and load in `onnxruntime` with `providers=['DmlExecutionProvider']`. Broader op coverage than torch-directml. Requires an ONNX-exportable model. See `onnxruntime-arm64-01`.");
        sb.AppendLine("3. **ONNX Runtime + QNN EP** — same shape as #2 but with `providers=['QNNExecutionProvider']` for Snapdragon NPU acceleration. Best latency, tightest op restrictions. See `onnxruntime-arm64-01`.");
        sb.AppendLine("4. **CPU fallback** — remove all `.cuda()` / `.to(\"cuda\")` and let ops run on the Snapdragon CPU cores. Slowest, but works with an unmodified model.");
        sb.AppendLine();
        sb.AppendLine("This audit does **not** pick an option. Each site above should be reviewed alongside the model's performance and op-coverage requirements before a routing decision is committed.");
        return sb.ToString();
    }

    private static string EscapeMd(string s) => s.Replace("|", "\\|").Replace("`", "\\`");

    private readonly record struct Hit(string File, IReadOnlyList<(int Line, string Snippet)> Lines);
}
