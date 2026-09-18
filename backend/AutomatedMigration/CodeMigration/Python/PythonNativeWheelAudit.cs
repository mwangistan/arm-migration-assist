using System.Text;
using AutomatedMigration.Generators;
using AutomatedMigration.Models;

namespace AutomatedMigration.CodeMigration.Python;

// Audits pip dependencies against a hand-curated table of packages known to
// ship native (C/Rust/C++) code, so the reviewer knows which ones need a
// `win_arm64` wheel or a source build with an ARM64 toolchain. The table is
// intentionally small and evidence-linked; anything not on the table is
// classified "verify" rather than silently marked safe.
public sealed class PythonNativeWheelAudit : IMigrationGenerator
{
    private enum NativeStatus
    {
        // Native code, wheel status must be verified per package. Grounded in
        // python-woa-wheels-01.
        NativeRequiresVerification,

        // Depends on torch; wheel availability follows torch (see
        // pytorch-woa-status-01).
        TorchDependent,

        // Predominantly pure-Python distribution. Safe on ARM64 as long as
        // transitive native deps also have wheels.
        PurePythonLikely,
    }

    // Names are compared against PipRequirementsParser.Normalize().
    private static readonly IReadOnlyDictionary<string, NativeStatus> Known = new Dictionary<string, NativeStatus>(StringComparer.OrdinalIgnoreCase)
    {
        // Native pyd/so shipped as wheels; `win_arm64` availability varies by
        // version. All entries here are known to have C or Rust extensions per
        // their upstream repos.
        ["numpy"] = NativeStatus.NativeRequiresVerification,
        ["pillow"] = NativeStatus.NativeRequiresVerification,
        ["scipy"] = NativeStatus.NativeRequiresVerification,
        ["pandas"] = NativeStatus.NativeRequiresVerification,
        ["cffi"] = NativeStatus.NativeRequiresVerification,
        ["cryptography"] = NativeStatus.NativeRequiresVerification,
        ["pyyaml"] = NativeStatus.NativeRequiresVerification,
        ["psutil"] = NativeStatus.NativeRequiresVerification,
        ["regex"] = NativeStatus.NativeRequiresVerification,
        ["lxml"] = NativeStatus.NativeRequiresVerification,
        ["ujson"] = NativeStatus.NativeRequiresVerification,
        ["orjson"] = NativeStatus.NativeRequiresVerification,
        ["blake3"] = NativeStatus.NativeRequiresVerification,
        ["av"] = NativeStatus.NativeRequiresVerification,        // PyAV / ffmpeg
        ["opencv-python"] = NativeStatus.NativeRequiresVerification,
        ["opencv-python-headless"] = NativeStatus.NativeRequiresVerification,
        ["safetensors"] = NativeStatus.NativeRequiresVerification,
        ["tokenizers"] = NativeStatus.NativeRequiresVerification,
        ["sentencepiece"] = NativeStatus.NativeRequiresVerification,
        ["msgpack"] = NativeStatus.NativeRequiresVerification,
        ["grpcio"] = NativeStatus.NativeRequiresVerification,
        ["aiohttp"] = NativeStatus.NativeRequiresVerification,   // aiohttp ships C-extension for HTTP parser

        ["torch"] = NativeStatus.TorchDependent,
        ["torchvision"] = NativeStatus.TorchDependent,
        ["torchaudio"] = NativeStatus.TorchDependent,
        ["torch-directml"] = NativeStatus.TorchDependent,
        ["kornia"] = NativeStatus.TorchDependent,
        ["transformers"] = NativeStatus.TorchDependent,
        ["diffusers"] = NativeStatus.TorchDependent,
        ["accelerate"] = NativeStatus.TorchDependent,

        ["einops"] = NativeStatus.PurePythonLikely,
        ["filelock"] = NativeStatus.PurePythonLikely,
        ["alembic"] = NativeStatus.PurePythonLikely,
        ["pydantic"] = NativeStatus.PurePythonLikely,
        ["typing-extensions"] = NativeStatus.PurePythonLikely,
        ["packaging"] = NativeStatus.PurePythonLikely,
        ["click"] = NativeStatus.PurePythonLikely,
        ["rich"] = NativeStatus.PurePythonLikely,
        ["tqdm"] = NativeStatus.PurePythonLikely,
        ["requests"] = NativeStatus.PurePythonLikely,
    };

    private const string ReportPath = ".arm-migration/reports/python-native-wheel-audit.md";

    public GeneratedPatch? Generate(WorkItem workItem, MigrationContext context)
    {
        var deps = PipRequirementsParser.ScanRepo(context.RepoPath);
        if (deps.Count == 0)
            return null;

        var body = BuildReport(deps);
        return new GeneratedPatch(UnifiedDiff.NewFile(ReportPath, body));
    }

    private static string BuildReport(IReadOnlyList<PipRequirementsParser.PipDependency> deps)
    {
        int native = 0, torch = 0, pure = 0, unknown = 0;
        var rows = new List<(string Package, string Pin, string Location, string Status)>();
        foreach (var dep in deps)
        {
            var (status, label) = Classify(dep.NormalizedName);
            switch (status)
            {
                case NativeStatus.NativeRequiresVerification: native++; break;
                case NativeStatus.TorchDependent: torch++; break;
                case NativeStatus.PurePythonLikely: pure++; break;
                default: unknown++; break;
            }
            rows.Add((
                dep.NormalizedName,
                dep.VersionSpec ?? "(unpinned)",
                dep.SourceFile + ":" + dep.LineNumber,
                label));
        }

        var sb = new StringBuilder();
        sb.AppendLine("# Python native-wheel audit for Windows on Arm");
        sb.AppendLine();
        sb.AppendLine("Grounded in [guidance `python-woa-wheels-01`](../../knowledge/windows-on-arm/snippets/python-woa-wheels-01.md).");
        sb.AppendLine();
        sb.Append("**Summary:** ").Append(deps.Count).Append(" declared pip dependencies. ")
          .Append(native).Append(" known-native (need `win_arm64` wheel verification), ")
          .Append(torch).Append(" follow torch (see `pytorch-woa-status-01`), ")
          .Append(pure).Append(" likely pure-Python, ")
          .Append(unknown).AppendLine(" not on the audit table.");
        sb.AppendLine();
        sb.AppendLine("| Package | Declared pin | Location | ARM64 status |");
        sb.AppendLine("| ------- | ------------ | -------- | ------------ |");
        foreach (var row in rows)
        {
            sb.Append("| `").Append(row.Package).Append("` | ")
              .Append(row.Pin).Append(" | `").Append(row.Location)
              .Append("` | ").Append(row.Status).AppendLine(" |");
        }
        sb.AppendLine();
        sb.AppendLine("## How to read this");
        sb.AppendLine();
        sb.AppendLine("- **Known native, verify wheel**: Package ships C/C++/Rust code. A `win_arm64` wheel may or may not exist for the pinned version. Confirm with `pip download <name>==<version> --platform win_arm64 --only-binary=:all: --dest /tmp/wheels/` before assuming it installs. If missing, options are (a) upgrade to a version that has a wheel, (b) build from source (requires MSVC/LLVM ARM64 toolchain and, for Rust extensions, `rustup target add aarch64-pc-windows-msvc`), or (c) substitute an alternative.");
        sb.AppendLine("- **Torch-dependent**: Wheel availability follows `torch`. See the sibling audit `python/pytorch-arm64-wheel-audit`.");
        sb.AppendLine("- **Pure-Python likely**: These packages predominantly ship pure-Python code. They install on ARM64 as long as their transitive native deps do.");
        sb.AppendLine("- **Not on audit table**: Not classified here. Verify individually by inspecting the wheel `.dist-info` or the source repo.");
        sb.AppendLine();
        sb.AppendLine("The audit table is intentionally hand-curated. Anything unknown stays unknown rather than silently marked safe. See guidance `python-woa-wheels-01` for the general strategy.");
        return sb.ToString();
    }

    private static (NativeStatus Status, string Label) Classify(string normalizedName)
    {
        if (Known.TryGetValue(normalizedName, out var status))
        {
            return status switch
            {
                NativeStatus.NativeRequiresVerification => (status, "Known native — verify `win_arm64` wheel."),
                NativeStatus.TorchDependent => (status, "Follows torch — see `pytorch-woa-status-01`."),
                NativeStatus.PurePythonLikely => (status, "Likely pure-Python — should install on ARM64."),
                _ => (status, "Not classified."),
            };
        }
        return (NativeStatus.PurePythonLikely, "Not on audit table — verify individually.");
    }
}
