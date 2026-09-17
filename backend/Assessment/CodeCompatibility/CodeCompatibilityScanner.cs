using System.Security.Cryptography;
using System.Text;
using ArmMigrationAssist.RepositoryDiscovery.Models;
using ArmMigrationAssist.RepositoryDiscovery.Scanning;

namespace ArmMigrationAssist.Assessment.CodeCompatibility;

internal sealed class CodeCompatibilityScanner
{
    private const int MaximumFindings = 20_000;

    private static readonly HashSet<string> ScannableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".asm", ".c", ".cc", ".cpp", ".cs", ".cxx", ".h", ".hpp", ".py", ".rs", ".s",
    };

    private static readonly IReadOnlyList<CompatibilityRule> Rules =
    [
        new(
            "ARM-CODE-PINVOKE-01",
            "p-invoke",
            "medium",
            line => ContainsAny(line, "[DllImport(", "[LibraryImport("),
            "A managed native-library import is declared.",
            "Detected a managed native-library import declaration.",
            0.95m),
        new(
            "ARM-CODE-INLINE-ASM-01",
            "inline-asm",
            "high",
            line => ContainsAny(line, "__asm", " asm(", "asm volatile", "__asm__("),
            "Inline assembly requires architecture-specific review.",
            "Detected an inline assembly construct.",
            0.95m),
        new(
            "ARM-CODE-SIMD-01",
            "simd",
            "high",
            line => ContainsAny(
                line,
                "<immintrin.h>",
                "<emmintrin.h>",
                "<xmmintrin.h>",
                "<nmmintrin.h>",
                "<smmintrin.h>",
                "<tmmintrin.h>",
                "<pmmintrin.h>",
                "_mm_",
                "_mm256_",
                "_mm512_",
                "__m128",
                "__m256",
                "__m512"),
            "An x86 SIMD intrinsic or header is referenced.",
            "Detected an x86 SIMD intrinsic or header reference.",
            0.95m),
        new(
            "ARM-CODE-ARCH-MACRO-01",
            "architecture-conditional",
            "medium",
            line => ContainsAny(
                line,
                "_M_X64",
                "_M_AMD64",
                "_M_IX86",
                "__x86_64__",
                "__i386__",
                "__amd64__",
                "_AMD64_",
                "_X86_"),
            "An x86-family architecture conditional is present.",
            "Detected an x86-family architecture macro.",
            0.9m),
        new(
            "ARM-CODE-POINTER-SIZE-01",
            "pointer-size",
            "medium",
            line => ContainsAny(line, ".ToInt32()", "sizeof(void*)", "sizeof (void*)"),
            "A pointer-size-sensitive expression is present.",
            "Detected a pointer-size-sensitive expression.",
            0.85m),
        new(
            "ARM-CODE-DYNAMIC-NATIVE-01",
            "native-loading",
            "medium",
            line => ContainsAny(line, "NativeLibrary.Load(", "LoadLibrary(", "LoadLibraryW(", "ctypes.CDLL(", "ctypes.WinDLL("),
            "A native library is loaded dynamically.",
            "Detected a dynamic native-library load.",
            0.9m),
    ];

    public IReadOnlyList<CodeFinding> Scan(
        IReadOnlyList<RepositoryFile> files,
        CancellationToken cancellationToken = default)
    {
        var findings = new List<CodeFinding>();
        foreach (var file in files.Where(file => file.Content is not null && ScannableExtensions.Contains(file.Extension)))
        {
            if (findings.Count >= MaximumFindings)
            {
                break;
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (file.Extension is ".asm" or ".s")
            {
                findings.Add(new CodeFinding(
                    StableId(file.RelativePath, "ARM-CODE-ASSEMBLY-SOURCE-01", 1),
                    "ARM-CODE-ASSEMBLY-SOURCE-01",
                    "assembly-source",
                    "high",
                    file.RelativePath,
                    1,
                    null,
                    "Assembly source requires an ARM64 implementation or guarded alternative.",
                    [new Evidence("file", file.RelativePath, null, "Detected an assembly source file.")],
                    0.99m));
            }

            var lines = file.Content!.Split('\n');
            for (var lineIndex = 0; lineIndex < lines.Length && findings.Count < MaximumFindings; lineIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var line = lines[lineIndex];
                foreach (var rule in Rules)
                {
                    if (findings.Count >= MaximumFindings)
                    {
                        break;
                    }

                    if (!rule.IsMatch(line))
                    {
                        continue;
                    }

                    var lineNumber = lineIndex + 1;
                    findings.Add(new CodeFinding(
                        StableId(file.RelativePath, rule.RuleId, lineNumber),
                        rule.RuleId,
                        rule.Category,
                        rule.Severity,
                        file.RelativePath,
                        lineNumber,
                        null,
                        rule.Description,
                        [new Evidence("file", file.RelativePath, null, rule.Observation)],
                        rule.Confidence));
                }
            }
        }

        return findings
            .OrderBy(finding => finding.File, StringComparer.Ordinal)
            .ThenBy(finding => finding.Line)
            .ThenBy(finding => finding.RuleId, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool ContainsAny(string line, params string[] values) =>
        values.Any(value => line.Contains(value, StringComparison.OrdinalIgnoreCase));

    private static string StableId(string path, string ruleId, int line)
    {
        var identity = $"{path}|{ruleId}|{line}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
        return $"code-{hash[..20]}";
    }

    private sealed record CompatibilityRule(
        string RuleId,
        string Category,
        string Severity,
        Func<string, bool> IsMatch,
        string Description,
        string Observation,
        decimal Confidence);
}